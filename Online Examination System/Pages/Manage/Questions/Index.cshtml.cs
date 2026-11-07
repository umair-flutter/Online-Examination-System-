using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Manage.Questions;

public class IndexModel(ApplicationDbContext db, ExcelService excel, PdfService pdf, AuditService audit, FileStorageService files) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int? SubjectId { get; set; }
    [BindProperty(SupportsGet = true)] public QuestionType? Type { get; set; }
    [BindProperty(SupportsGet = true)] public Difficulty? Difficulty { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;
    public PagedResult<Question> Items { get; set; } = null!;
    public List<SelectListItem> Subjects { get; set; } = [];
    public Dictionary<int, int> UsageCount { get; set; } = [];

    private IQueryable<Question> Query()
    {
        var q = db.Questions.Include(x => x.Subject).Include(x => x.CreatedBy).AsQueryable();
        if (!User.IsAdmin()) { var uid = User.UserId(); q = q.Where(x => x.CreatedById == uid); }
        if (!string.IsNullOrWhiteSpace(Search)) q = q.Where(x => x.Text.Contains(Search) || (x.Tags != null && x.Tags.Contains(Search)));
        if (SubjectId is int s) q = q.Where(x => x.SubjectId == s);
        if (Type is { } t) q = q.Where(x => x.Type == t);
        if (Difficulty is { } d) q = q.Where(x => x.Difficulty == d);
        return q.OrderByDescending(x => x.Id);
    }

    public async Task OnGetAsync()
    {
        Subjects = await db.Subjects.OrderBy(s => s.Name).Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync();
        Items = await Query().AsNoTracking().ToPagedAsync(PageNo, 15);
        var ids = Items.Items.Select(i => i.Id).ToList();
        UsageCount = await db.ExamQuestions.Where(e => ids.Contains(e.QuestionId)).GroupBy(e => e.QuestionId)
            .ToDictionaryAsync(g => g.Key, g => g.Count());
    }

    public async Task<IActionResult> OnGetExcelAsync()
    {
        var list = await Query().Include(q => q.Choices).AsNoTracking().ToListAsync();
        return File(excel.ExportQuestions(list), ExcelService.ContentType, "questions.xlsx");
    }

    public async Task<IActionResult> OnGetPdfAsync()
    {
        var list = await Query().Include(q => q.Choices).AsNoTracking().ToListAsync();
        var rows = list.Select(q => (IReadOnlyList<object?>)new object?[]
        {
            q.Id, q.Subject.Code, q.Type, q.Text.Truncate(120), q.Marks, q.Difficulty,
            q.Type == QuestionType.ShortAnswer ? q.CorrectShortAnswer : string.Join(" | ", q.Choices.Where(c => c.IsCorrect).Select(c => c.Text))
        });
        return File(pdf.Table("Question bank", $"{list.Count} question(s)", ["#", "Subject", "Type", "Question", "Marks", "Difficulty", "Answer"], rows, landscape: true),
            PdfService.ContentType, "questions.pdf");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var q = await db.Questions.FindAsync(id);
        if (q is null) return NotFound();
        if (!User.IsAdmin() && q.CreatedById != User.UserId()) return Forbid();
        if (await db.AttemptAnswers.AnyAsync(a => a.QuestionId == id))
        {
            TempData[Toast.Error] = "This question has already been answered by students and cannot be deleted.";
            return RedirectToPage();
        }
        db.ExamQuestions.RemoveRange(db.ExamQuestions.Where(e => e.QuestionId == id));
        db.Questions.Remove(q);
        await db.SaveChangesAsync();
        files.DeletePublicFile(q.ImagePath);
        await audit.LogAsync("Delete", "Question", id, q.Text.Truncate(100));
        TempData[Toast.Success] = "Question deleted.";
        return RedirectToPage();
    }
}
