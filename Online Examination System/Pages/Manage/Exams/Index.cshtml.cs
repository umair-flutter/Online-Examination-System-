using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Manage.Exams;

public record ExamRow(int Id, string Title, string Subject, ExamStatus Status, DateTime StartDate, DateTime EndDate, int Duration,
    int Questions, double TotalMarks, int Attempts, int Pending, string Owner, string Classes);

public class IndexModel(ApplicationDbContext db, ExcelService excel, PdfService pdf, AuditService audit, NotificationService notify) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int? SubjectId { get; set; }
    [BindProperty(SupportsGet = true)] public ExamStatus? Status { get; set; }
    /// <summary>open | upcoming | closed</summary>
    [BindProperty(SupportsGet = true)] public string? State { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;
    public PagedResult<ExamRow> Items { get; set; } = null!;
    public List<SelectListItem> Subjects { get; set; } = [];

    private IQueryable<ExamRow> Query()
    {
        var now = DateTime.Now;
        var q = db.Exams.OwnedBy(User);
        if (!string.IsNullOrWhiteSpace(Search)) q = q.Where(e => e.Title.Contains(Search));
        if (SubjectId is int s) q = q.Where(e => e.SubjectId == s);
        if (Status is { } st) q = q.Where(e => e.Status == st);
        q = State switch
        {
            "open" => q.Where(e => e.Status == ExamStatus.Published && e.StartDate <= now && e.EndDate >= now),
            "upcoming" => q.Where(e => e.StartDate > now),
            "closed" => q.Where(e => e.EndDate < now),
            _ => q
        };
        return q.OrderByDescending(e => e.StartDate).Select(e => new ExamRow(e.Id, e.Title, e.Subject.Name, e.Status, e.StartDate, e.EndDate, e.DurationMinutes,
            e.Questions.Count, e.Questions.Sum(x => (double?)x.Question.Marks) ?? 0,
            e.Attempts.Count(a => a.Status != AttemptStatus.InProgress), e.Attempts.Count(a => a.Status == AttemptStatus.PendingMarking),
            e.CreatedBy.FullName, string.Join(", ", e.Classes.Select(c => c.ClassRoom.Name))));
    }

    public async Task OnGetAsync()
    {
        Subjects = await db.Subjects.OrderBy(s => s.Name).Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync();
        Items = await Query().ToPagedAsync(PageNo);
    }

    private static readonly string[] Headers = ["Title", "Subject", "Status", "Start", "End", "Minutes", "Questions", "Total marks", "Submissions", "Classes", "Owner"];
    private static IReadOnlyList<object?> Row(ExamRow e) =>
        [e.Title, e.Subject, e.Status, e.StartDate, e.EndDate, e.Duration, e.Questions, e.TotalMarks, e.Attempts, e.Classes, e.Owner];

    public async Task<IActionResult> OnGetExcelAsync() =>
        File(excel.Export("Exams", Headers, (await Query().ToListAsync()).Select(Row)), ExcelService.ContentType, "exams.xlsx");

    public async Task<IActionResult> OnGetPdfAsync() =>
        File(pdf.Table("Exams", null, Headers, (await Query().ToListAsync()).Select(Row), landscape: true), PdfService.ContentType, "exams.pdf");

    public async Task<IActionResult> OnPostPublishAsync(int id, bool publish)
    {
        var e = await db.Exams.FindAsync(id);
        if (e is null) return NotFound();
        if (!User.CanManage(e)) return Forbid();
        if (publish && !await db.ExamQuestions.AnyAsync(x => x.ExamId == id))
        {
            TempData[Toast.Error] = "Add at least one question before publishing.";
            return RedirectToPage();
        }
        var wasPublished = e.Status == ExamStatus.Published;
        e.Status = publish ? ExamStatus.Published : ExamStatus.Draft;
        await db.SaveChangesAsync();
        if (publish && !wasPublished && e.EndDate > DateTime.Now) await notify.NotifyExamPublishedAsync(e);
        await audit.LogAsync(publish ? "Publish" : "Unpublish", "Exam", id, e.Title);
        TempData[Toast.Success] = publish ? "Exam published. Students have been notified." : "Exam moved back to draft.";
        return RedirectToPage();
    }

    /// <summary>Clone exam: copies settings, classes and questions into a new draft.</summary>
    public async Task<IActionResult> OnPostCloneAsync(int id)
    {
        var e = await db.Exams.AsNoTracking().Include(x => x.Questions).Include(x => x.Classes).FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        if (!User.CanManage(e)) return Forbid();
        var copy = new Exam
        {
            Title = $"{e.Title} (Copy)", Description = e.Description, Instructions = e.Instructions, SubjectId = e.SubjectId,
            StartDate = e.StartDate, EndDate = e.EndDate, DurationMinutes = e.DurationMinutes, PassPercentage = e.PassPercentage,
            ShowAnswersToStudents = e.ShowAnswersToStudents, RandomizeQuestions = e.RandomizeQuestions, RandomizeChoices = e.RandomizeChoices,
            AllowBackNavigation = e.AllowBackNavigation, MaxAttempts = e.MaxAttempts, NegativeMarking = e.NegativeMarking,
            PartialCreditMultipleAnswer = e.PartialCreditMultipleAnswer, AccessCode = e.AccessCode, EnableProctoring = e.EnableProctoring,
            MaxViolations = e.MaxViolations, Status = ExamStatus.Draft, CreatedById = User.UserId(),
            Questions = e.Questions.Select(q => new ExamQuestion { QuestionId = q.QuestionId, Order = q.Order }).ToList(),
            Classes = e.Classes.Select(c => new ExamClass { ClassRoomId = c.ClassRoomId }).ToList()
        };
        db.Exams.Add(copy);
        await db.SaveChangesAsync();
        await audit.LogAsync("Clone", "Exam", copy.Id, $"from #{id} {e.Title}");
        TempData[Toast.Success] = "Exam cloned as a draft. Review the dates before publishing.";
        return RedirectToPage("Edit", new { id = copy.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var e = await db.Exams.FindAsync(id);
        if (e is null) return NotFound();
        if (!User.CanManage(e)) return Forbid();
        var attempts = await db.ExamAttempts.CountAsync(a => a.ExamId == id);
        db.Exams.Remove(e);
        await db.SaveChangesAsync();
        await audit.LogAsync("Delete", "Exam", id, $"{e.Title} ({attempts} attempt(s) removed)");
        TempData[Toast.Success] = "Exam deleted.";
        return RedirectToPage();
    }
}
