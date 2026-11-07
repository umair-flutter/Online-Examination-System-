using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Student;

public class ResultModel(ApplicationDbContext db, PdfService pdf) : PageModel
{
    public ExamAttempt Attempt { get; set; } = null!;
    public Dictionary<int, Question> Questions { get; set; } = [];
    /// <summary>Answers are revealed only if the teacher allows it AND the student cannot take the exam again (closed or no attempts left).</summary>
    public bool CanReview { get; set; }
    public bool ReviewLater { get; set; }
    public int Rank { get; set; }
    public int Participants { get; set; }
    public double ClassAverage { get; set; }

    private async Task<IActionResult?> LoadAsync(int id)
    {
        var a = await db.ExamAttempts.Include(x => x.Exam).ThenInclude(e => e.Subject).Include(x => x.Student).Include(x => x.Answers)
            .AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (a is null || a.StudentId != User.UserId()) return NotFound();
        if (!a.IsSubmitted) return RedirectToPage("Take", new { id });
        Attempt = a;
        var ids = a.QuestionIds;
        Questions = await db.Questions.Include(q => q.Choices).Where(q => ids.Contains(q.Id)).AsNoTracking().ToDictionaryAsync(q => q.Id);

        var attemptsUsed = await db.ExamAttempts.CountAsync(x => x.ExamId == a.ExamId && x.StudentId == a.StudentId);
        var finished = DateTime.Now > a.Exam.EndDate || (a.Exam.MaxAttempts > 0 && attemptsUsed >= a.Exam.MaxAttempts);
        CanReview = a.Exam.ShowAnswersToStudents && finished;
        ReviewLater = a.Exam.ShowAnswersToStudents && !finished;
        return null;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (await LoadAsync(id) is { } r) return r;
        if (Attempt.Status == AttemptStatus.Graded)
        {
            // best graded attempt per student
            var best = await db.ExamAttempts.Where(x => x.ExamId == Attempt.ExamId && x.Status == AttemptStatus.Graded)
                .GroupBy(x => x.StudentId).Select(g => g.Max(x => x.Percentage)).ToListAsync();
            Participants = best.Count;
            ClassAverage = best.Count > 0 ? Math.Round(best.Average(), 1) : 0;
            Rank = best.Count(p => p > Attempt.Percentage) + 1;
        }
        return Page();
    }

    public async Task<IActionResult> OnGetPdfAsync(int id)
    {
        if (await LoadAsync(id) is { } r) return r;
        return File(pdf.ResultSlip(Attempt, Questions, CanReview), PdfService.ContentType, $"result-{id}.pdf");
    }

    public async Task<IActionResult> OnGetCertificateAsync(int id)
    {
        if (await LoadAsync(id) is { } r) return r;
        if (!Attempt.IsPassed || Attempt.CertificateCode is null) return NotFound();
        var verify = $"{Request.Scheme}://{Request.Host}/Verify?code={Attempt.CertificateCode}";
        return File(pdf.Certificate(Attempt, verify), PdfService.ContentType, $"certificate-{Attempt.CertificateCode}.pdf");
    }
}
