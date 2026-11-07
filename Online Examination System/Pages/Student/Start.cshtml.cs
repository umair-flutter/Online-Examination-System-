using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Student;

public class StartModel(ApplicationDbContext db, ExamService exams, AuditService audit) : PageModel
{
    public Exam Exam { get; set; } = null!;
    public int QuestionCount { get; set; }
    public double TotalMarks { get; set; }
    public List<ExamAttempt> Attempts { get; set; } = [];
    public ExamAvailability Availability { get; set; }
    [BindProperty] public string? AccessCode { get; set; }
    [BindProperty] public bool Agree { get; set; }

    private async Task<bool> LoadAsync(int id)
    {
        var uid = User.UserId();
        await exams.FinalizeExpiredAttemptsAsync(uid);
        var e = await exams.VisibleExamsFor(uid).Include(x => x.Subject).FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return false;
        Exam = e;
        QuestionCount = await db.ExamQuestions.CountAsync(q => q.ExamId == id);
        TotalMarks = await db.ExamQuestions.Where(q => q.ExamId == id).SumAsync(q => q.Question.Marks);
        Attempts = await db.ExamAttempts.Where(a => a.ExamId == id && a.StudentId == uid).OrderBy(a => a.AttemptNumber).ToListAsync();
        Availability = ExamService.GetAvailability(e, Attempts, DateTime.Now);
        return true;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (Availability == ExamAvailability.InProgress)
            return RedirectToPage("Take", new { id = Attempts.First(a => !a.IsSubmitted).Id });
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (Availability == ExamAvailability.InProgress)
            return RedirectToPage("Take", new { id = Attempts.First(a => !a.IsSubmitted).Id });
        if (Availability != ExamAvailability.Open)
        {
            TempData[Toast.Error] = Availability switch
            {
                ExamAvailability.NotStarted => "This exam has not started yet.",
                ExamAvailability.Closed => "This exam is closed.",
                _ => "You have used all your attempts for this exam."
            };
            return RedirectToPage(new { id });
        }
        if (QuestionCount == 0) { TempData[Toast.Error] = "This exam has no questions yet."; return RedirectToPage(new { id }); }
        if (!string.IsNullOrEmpty(Exam.AccessCode) && !string.Equals(Exam.AccessCode, AccessCode?.Trim(), StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(AccessCode), "The access code is incorrect.");
            await audit.LogAsync("WrongAccessCode", "Exam", id);
            return Page();
        }
        if (!Agree) { ModelState.AddModelError(nameof(Agree), "Please confirm that you have read the rules."); return Page(); }

        var attempt = await exams.StartAttemptAsync(Exam, User.UserId(), HttpContext.Connection.RemoteIpAddress?.ToString());
        await audit.LogAsync("StartExam", "ExamAttempt", attempt.Id, Exam.Title);
        return RedirectToPage("Take", new { id = attempt.Id });
    }
}
