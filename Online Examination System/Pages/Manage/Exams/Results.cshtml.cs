using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Manage.Exams;

public record ItemStat(int QuestionId, int Order, string Text, QuestionType Type, double Marks, int Answered, int Correct, double AvgMarks)
{
    /// <summary>Difficulty index: share of students who got it right.</summary>
    public double CorrectRate => Answered == 0 ? 0 : Math.Round(Correct * 100.0 / Answered, 1);
}

public class ResultsModel(ApplicationDbContext db, GradingService grading, ExcelService excel, PdfService pdf, AuditService audit) : PageModel
{
    public Exam Exam { get; set; } = null!;
    public List<ExamAttempt> Attempts { get; set; } = [];
    public int InProgress { get; set; }
    public int Eligible { get; set; }
    public double Average { get; set; }
    public double Highest { get; set; }
    public double Lowest { get; set; }
    public double PassRate { get; set; }
    public double AvgMinutes { get; set; }
    public int[] Histogram { get; set; } = new int[10];
    public List<ItemStat> Items { get; set; } = [];

    private async Task<IActionResult?> LoadAsync(int id)
    {
        var e = await db.Exams.Include(x => x.Subject).Include(x => x.Classes).ThenInclude(c => c.ClassRoom).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        if (!User.CanManage(e)) return Forbid();
        Exam = e;
        Attempts = await db.ExamAttempts.Include(a => a.Student).Where(a => a.ExamId == id && a.Status != AttemptStatus.InProgress)
            .OrderByDescending(a => a.Percentage).ThenBy(a => a.SubmittedAt).AsNoTracking().ToListAsync();
        return null;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (await LoadAsync(id) is { } r) return r;
        InProgress = await db.ExamAttempts.CountAsync(a => a.ExamId == id && a.Status == AttemptStatus.InProgress);
        var classIds = Exam.Classes.Select(c => c.ClassRoomId).ToList();
        if (classIds.Count > 0)
            Eligible = await db.ClassStudents.Where(c => classIds.Contains(c.ClassRoomId)).Select(c => c.StudentId).Distinct().CountAsync();
        else
        {
            var role = await db.Roles.Where(r => r.Name == Roles.Student).Select(r => r.Id).FirstAsync();
            Eligible = await db.UserRoles.CountAsync(r => r.RoleId == role);
        }

        var graded = Attempts.Where(a => a.Status == AttemptStatus.Graded).ToList();
        if (graded.Count > 0)
        {
            Average = Math.Round(graded.Average(a => a.Percentage), 1);
            Highest = graded.Max(a => a.Percentage);
            Lowest = graded.Min(a => a.Percentage);
            PassRate = Math.Round(graded.Count(a => a.IsPassed) * 100.0 / graded.Count, 1);
            foreach (var a in graded) Histogram[Math.Min(9, (int)(a.Percentage / 10))]++;
        }
        var timed = Attempts.Where(a => a.SubmittedAt != null).ToList();
        if (timed.Count > 0) AvgMinutes = Math.Round(timed.Average(a => (a.SubmittedAt!.Value - a.StartedAt).TotalMinutes), 1);

        // Item analysis
        var questions = await db.ExamQuestions.Where(x => x.ExamId == id).OrderBy(x => x.Order)
            .Select(x => new { x.QuestionId, x.Order, x.Question.Text, x.Question.Type, x.Question.Marks }).ToListAsync();
        var stats = await db.AttemptAnswers.Where(a => a.Attempt.ExamId == id && a.Attempt.Status != AttemptStatus.InProgress)
            .GroupBy(a => a.QuestionId)
            .Select(g => new { g.Key, Answered = g.Count(), Correct = g.Count(x => x.IsCorrect == true), Avg = g.Average(x => x.MarksAwarded ?? 0) })
            .ToDictionaryAsync(x => x.Key);
        Items = questions.Select((q, i) =>
        {
            stats.TryGetValue(q.QuestionId, out var s);
            return new ItemStat(q.QuestionId, i + 1, q.Text, q.Type, q.Marks, s?.Answered ?? 0, s?.Correct ?? 0, Math.Round(s?.Avg ?? 0, 2));
        }).ToList();
        return Page();
    }

    private static readonly string[] Headers = ["Rank", "Student", "Username", "Student no.", "Attempt", "Started", "Submitted", "Score", "Total", "Percentage", "Result", "Violations", "Auto-submitted"];

    private IEnumerable<IReadOnlyList<object?>> Rows() => Attempts.Select((a, i) => (IReadOnlyList<object?>)new object?[]
    {
        i + 1, a.Student.FullName, a.Student.UserName, a.Student.StudentNumber, a.AttemptNumber, a.StartedAt, a.SubmittedAt, a.Score, a.TotalMarks,
        a.Percentage, a.Status == AttemptStatus.PendingMarking ? "Pending" : a.IsPassed ? "Pass" : "Fail", a.ViolationCount, a.AutoSubmitted
    });

    public async Task<IActionResult> OnGetExcelAsync(int id)
    {
        if (await LoadAsync(id) is { } r) return r;
        return File(excel.Export("Results", Headers, Rows()), ExcelService.ContentType, $"results-{id}.xlsx");
    }

    public async Task<IActionResult> OnGetPdfAsync(int id)
    {
        if (await LoadAsync(id) is { } r) return r;
        return File(pdf.Table($"Results · {Exam.Title}", $"{Exam.Subject.Name} · pass mark {Exam.PassPercentage}%", Headers, Rows(), landscape: true),
            PdfService.ContentType, $"results-{id}.pdf");
    }

    public async Task<IActionResult> OnPostRegradeAsync(int id)
    {
        if (await LoadAsync(id) is { } r) return r;
        var n = await grading.RegradeExamAsync(id);
        await audit.LogAsync("Regrade", "Exam", id, $"{n} attempt(s)");
        TempData[Toast.Success] = $"{n} result(s) re-graded.";
        return RedirectToPage(new { id });
    }

    /// <summary>Deletes an attempt so the student can sit the exam again.</summary>
    public async Task<IActionResult> OnPostResetAsync(int id, int attemptId)
    {
        if (await LoadAsync(id) is { } r) return r;
        var a = await db.ExamAttempts.Include(x => x.Student).FirstOrDefaultAsync(x => x.Id == attemptId && x.ExamId == id);
        if (a is null) return NotFound();
        db.ExamAttempts.Remove(a);
        await db.SaveChangesAsync();
        await audit.LogAsync("ResetAttempt", "Exam", id, $"{a.Student.UserName} attempt #{a.AttemptNumber}");
        TempData[Toast.Success] = $"Attempt of {a.Student.FullName} removed. The student can take the exam again.";
        return RedirectToPage(new { id });
    }
}
