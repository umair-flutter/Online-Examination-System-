using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;

namespace Online_Examination_System.Pages.Manage;

/// <summary>Queue of submissions with essay answers waiting for manual marking.</summary>
public class MarkingModel(ApplicationDbContext db) : PageModel
{
    public record Row(int Id, string Student, string UserName, string Exam, int ExamId, DateTime? SubmittedAt, int ToMark);

    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public int? ExamId { get; set; }
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true)] public bool ShowMarked { get; set; }
    [Microsoft.AspNetCore.Mvc.BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;
    public PagedResult<Row> Items { get; set; } = null!;
    public List<SelectListItem> ExamOptions { get; set; } = [];

    public async Task OnGetAsync()
    {
        var exams = db.Exams.OwnedBy(User);
        ExamOptions = await exams.Where(e => e.Questions.Any(q => q.Question.Type == QuestionType.Essay))
            .OrderBy(e => e.Title).Select(e => new SelectListItem(e.Title, e.Id.ToString())).ToListAsync();

        var q = db.ExamAttempts.Where(a => exams.Select(e => e.Id).Contains(a.ExamId));
        q = ShowMarked
            ? q.Where(a => a.Status != AttemptStatus.InProgress && a.Answers.Any(x => x.NeedsManualMarking))
            : q.Where(a => a.Status == AttemptStatus.PendingMarking);
        if (ExamId is int id) q = q.Where(a => a.ExamId == id);
        Items = await q.OrderBy(a => a.SubmittedAt)
            .Select(a => new Row(a.Id, a.Student.FullName, a.Student.UserName!, a.Exam.Title, a.ExamId, a.SubmittedAt,
                a.Answers.Count(x => x.NeedsManualMarking && !x.ManuallyMarked)))
            .ToPagedAsync(PageNo, 20);
    }
}
