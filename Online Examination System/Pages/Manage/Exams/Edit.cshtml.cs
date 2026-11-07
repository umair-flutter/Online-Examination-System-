using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Manage.Exams;

public class EditModel(ApplicationDbContext db, GradingService grading, NotificationService notify, AuditService audit) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public List<SelectListItem> Subjects { get; set; } = [];
    public List<SelectListItem> ClassOptions { get; set; } = [];
    public int SubmittedCount { get; set; }

    public class InputModel
    {
        public int Id { get; set; }
        [Required, StringLength(200)] public string Title { get; set; } = "";
        [StringLength(2000)] public string? Description { get; set; }
        [StringLength(4000)] public string? Instructions { get; set; }
        [Required, Display(Name = "Subject")] public int? SubjectId { get; set; }
        [Required, Display(Name = "Start date")] public DateTime StartDate { get; set; } = DateTime.Today.AddDays(1).AddHours(9);
        [Required, Display(Name = "End date")] public DateTime EndDate { get; set; } = DateTime.Today.AddDays(8).AddHours(17);
        [Range(1, 1440), Display(Name = "Duration (minutes)")] public int DurationMinutes { get; set; } = 30;
        [Range(0, 100), Display(Name = "Pass percentage")] public double PassPercentage { get; set; } = 50;
        public bool ShowAnswersToStudents { get; set; } = true;
        public bool RandomizeQuestions { get; set; }
        public bool RandomizeChoices { get; set; }
        public bool AllowBackNavigation { get; set; } = true;
        [Range(0, 100), Display(Name = "Max attempts")] public int MaxAttempts { get; set; } = 1;
        [Range(0, 1), Display(Name = "Negative marking")] public double NegativeMarking { get; set; }
        public bool PartialCreditMultipleAnswer { get; set; }
        [StringLength(50), Display(Name = "Access code")] public string? AccessCode { get; set; }
        public bool EnableProctoring { get; set; }
        [Range(0, 100), Display(Name = "Max violations")] public int MaxViolations { get; set; } = 3;
        public ExamStatus Status { get; set; } = ExamStatus.Draft;
        public List<int> ClassIds { get; set; } = [];
    }

    private async Task LoadAsync()
    {
        Subjects = await db.Subjects.OrderBy(s => s.Name).Select(s => new SelectListItem($"{s.Code} – {s.Name}", s.Id.ToString())).ToListAsync();
        ClassOptions = await db.ClassRooms.OrderBy(c => c.Name).Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync();
        if (Input.Id > 0) SubmittedCount = await db.ExamAttempts.CountAsync(a => a.ExamId == Input.Id && a.Status != AttemptStatus.InProgress);
    }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is not null)
        {
            var e = await db.Exams.Include(x => x.Classes).FirstOrDefaultAsync(x => x.Id == id);
            if (e is null) return NotFound();
            if (!User.CanManage(e)) return Forbid();
            Input = new InputModel
            {
                Id = e.Id, Title = e.Title, Description = e.Description, Instructions = e.Instructions, SubjectId = e.SubjectId,
                StartDate = e.StartDate, EndDate = e.EndDate, DurationMinutes = e.DurationMinutes, PassPercentage = e.PassPercentage,
                ShowAnswersToStudents = e.ShowAnswersToStudents, RandomizeQuestions = e.RandomizeQuestions, RandomizeChoices = e.RandomizeChoices,
                AllowBackNavigation = e.AllowBackNavigation, MaxAttempts = e.MaxAttempts, NegativeMarking = e.NegativeMarking,
                PartialCreditMultipleAnswer = e.PartialCreditMultipleAnswer, AccessCode = e.AccessCode, EnableProctoring = e.EnableProctoring,
                MaxViolations = e.MaxViolations, Status = e.Status, ClassIds = e.Classes.Select(c => c.ClassRoomId).ToList()
            };
        }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Input.EndDate <= Input.StartDate) ModelState.AddModelError("Input.EndDate", "End date must be after the start date.");
        Exam? e = null;
        if (Input.Id > 0)
        {
            e = await db.Exams.Include(x => x.Classes).FirstOrDefaultAsync(x => x.Id == Input.Id);
            if (e is null) return NotFound();
            if (!User.CanManage(e)) return Forbid();
        }
        if (Input.Status == ExamStatus.Published && (e is null || !await db.ExamQuestions.AnyAsync(q => q.ExamId == e.Id)))
            ModelState.AddModelError("Input.Status", "Add questions to the exam before publishing it. Save as draft first.");
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }

        var isNew = e is null;
        var gradingKeyBefore = e is null ? null : $"{e.PassPercentage}|{e.NegativeMarking}|{e.PartialCreditMultipleAnswer}";
        var wasPublished = e?.Status == ExamStatus.Published;
        if (e is null)
        {
            e = new Exam { CreatedById = User.UserId() };
            db.Exams.Add(e);
        }
        e.Title = Input.Title.Trim();
        e.Description = Input.Description;
        e.Instructions = Input.Instructions;
        e.SubjectId = Input.SubjectId!.Value;
        e.StartDate = Input.StartDate;
        e.EndDate = Input.EndDate;
        e.DurationMinutes = Input.DurationMinutes;
        e.PassPercentage = Input.PassPercentage;
        e.ShowAnswersToStudents = Input.ShowAnswersToStudents;
        e.RandomizeQuestions = Input.RandomizeQuestions;
        e.RandomizeChoices = Input.RandomizeChoices;
        e.AllowBackNavigation = Input.AllowBackNavigation;
        e.MaxAttempts = Input.MaxAttempts;
        e.NegativeMarking = Input.NegativeMarking;
        e.PartialCreditMultipleAnswer = Input.PartialCreditMultipleAnswer;
        e.AccessCode = string.IsNullOrWhiteSpace(Input.AccessCode) ? null : Input.AccessCode.Trim();
        e.EnableProctoring = Input.EnableProctoring;
        e.MaxViolations = Input.MaxViolations;
        e.Status = Input.Status;

        var wanted = Input.ClassIds.Distinct().ToHashSet();
        foreach (var c in e.Classes.Where(c => !wanted.Contains(c.ClassRoomId)).ToList()) e.Classes.Remove(c);
        foreach (var id in wanted.Where(id => e.Classes.All(c => c.ClassRoomId != id))) e.Classes.Add(new ExamClass { ClassRoomId = id });

        await db.SaveChangesAsync();

        var regraded = 0;
        if (!isNew && gradingKeyBefore != $"{e.PassPercentage}|{e.NegativeMarking}|{e.PartialCreditMultipleAnswer}")
            regraded = await grading.RegradeExamAsync(e.Id);
        if (e.Status == ExamStatus.Published && !wasPublished && e.EndDate > DateTime.Now)
            await notify.NotifyExamPublishedAsync(e);

        await audit.LogAsync(isNew ? "Create" : "Update", "Exam", e.Id, e.Title);
        TempData[Toast.Success] = regraded > 0 ? $"Exam saved. {regraded} result(s) were re-graded with the new settings." : "Exam saved.";
        return isNew ? RedirectToPage("Questions", new { id = e.Id }) : RedirectToPage("Index");
    }
}
