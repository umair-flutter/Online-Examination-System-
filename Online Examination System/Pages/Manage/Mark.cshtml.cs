using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Manage;

/// <summary>Mark essays and optionally override the marks of any other answer.</summary>
public class MarkModel(ApplicationDbContext db, GradingService grading, AuditService audit) : PageModel
{
    public ExamAttempt Attempt { get; set; } = null!;
    public Dictionary<int, Question> Questions { get; set; } = [];
    public int? NextId { get; set; }

    public class MarkInput
    {
        public int AnswerId { get; set; }
        public bool Override { get; set; }
        public double? Marks { get; set; }
        public string? Feedback { get; set; }
    }

    [BindProperty] public List<MarkInput> Marks { get; set; } = [];

    private async Task<IActionResult?> LoadAsync(int id)
    {
        var a = await db.ExamAttempts.Include(x => x.Exam).Include(x => x.Student).Include(x => x.Answers)
            .AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();
        if (!User.CanManage(a.Exam)) return Forbid();
        if (!a.IsSubmitted) { TempData[Toast.Error] = "This attempt is still in progress."; return RedirectToPage("/Manage/Marking"); }
        Attempt = a;
        var ids = a.QuestionIds;
        Questions = await db.Questions.Include(q => q.Choices).Where(q => ids.Contains(q.Id)).AsNoTracking().ToDictionaryAsync(q => q.Id);
        var exams = db.Exams.OwnedBy(User).Select(e => e.Id);
        NextId = await db.ExamAttempts.Where(x => x.Status == AttemptStatus.PendingMarking && x.Id != id && exams.Contains(x.ExamId))
            .OrderBy(x => x.SubmittedAt).Select(x => (int?)x.Id).FirstOrDefaultAsync();
        return null;
    }

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ?? Page();

    public async Task<IActionResult> OnPostAsync(int id, bool next)
    {
        if (await LoadAsync(id) is { } r) return r;
        var valid = Attempt.Answers.ToDictionary(a => a.Id);
        var dict = new Dictionary<int, (double, string?)>();
        foreach (var m in Marks)
        {
            if (!valid.TryGetValue(m.AnswerId, out var ans) || !Questions.TryGetValue(ans.QuestionId, out var q)) continue;
            var isEssay = q.Type == QuestionType.Essay;
            if (!isEssay && !m.Override) continue;
            if (m.Marks is null)
            {
                if (isEssay) continue; // left blank → still pending
                continue;
            }
            if (m.Marks < (isEssay ? 0 : -q.Marks) || m.Marks > q.Marks)
            {
                ModelState.AddModelError("", $"Marks for question \"{q.Text.Truncate(40)}\" must be between 0 and {q.Marks}.");
                continue;
            }
            dict[m.AnswerId] = (m.Marks.Value, string.IsNullOrWhiteSpace(m.Feedback) ? null : m.Feedback.Trim());
        }
        if (!ModelState.IsValid) return Page();

        await grading.SaveManualMarksAsync(id, dict, User.UserId());
        await audit.LogAsync("Mark", "ExamAttempt", id, $"{dict.Count} answer(s) marked for {Attempt.Student.UserName}");
        TempData[Toast.Success] = "Marks saved.";
        return next && NextId is int n ? RedirectToPage(new { id = n }) : RedirectToPage(new { id });
    }
}
