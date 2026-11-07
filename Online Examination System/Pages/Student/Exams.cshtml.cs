using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Student;

public record StudentExamCard(Exam Exam, int QuestionCount, double TotalMarks, List<ExamAttempt> Attempts, ExamAvailability Availability);

public class ExamsModel(ApplicationDbContext db, ExamService exams) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Tab { get; set; } = "open";
    public List<StudentExamCard> Cards { get; set; } = [];
    public Dictionary<string, int> Counts { get; set; } = [];

    public async Task OnGetAsync()
    {
        var uid = User.UserId();
        await exams.FinalizeExpiredAttemptsAsync(uid);
        var now = DateTime.Now;
        var list = await exams.VisibleExamsFor(uid).Include(e => e.Subject)
            .Select(e => new { Exam = e, Count = e.Questions.Count, Marks = e.Questions.Sum(q => (double?)q.Question.Marks) ?? 0 })
            .AsNoTracking().ToListAsync();
        var ids = list.Select(x => x.Exam.Id).ToList();
        var attempts = await db.ExamAttempts.Where(a => a.StudentId == uid && ids.Contains(a.ExamId)).AsNoTracking().ToListAsync();

        var all = list.Select(x =>
        {
            var mine = attempts.Where(a => a.ExamId == x.Exam.Id).OrderBy(a => a.AttemptNumber).ToList();
            return new StudentExamCard(x.Exam, x.Count, x.Marks, mine, ExamService.GetAvailability(x.Exam, mine, now));
        }).ToList();

        bool IsOpen(StudentExamCard c) => c.Availability is ExamAvailability.Open or ExamAvailability.InProgress;
        bool IsUpcoming(StudentExamCard c) => c.Availability == ExamAvailability.NotStarted;
        bool IsDone(StudentExamCard c) => !IsOpen(c) && !IsUpcoming(c);
        Counts = new() { ["open"] = all.Count(IsOpen), ["upcoming"] = all.Count(IsUpcoming), ["done"] = all.Count(IsDone) };

        Cards = (Tab switch
        {
            "upcoming" => all.Where(IsUpcoming).OrderBy(c => c.Exam.StartDate),
            "done" => all.Where(IsDone).OrderByDescending(c => c.Exam.EndDate),
            _ => all.Where(IsOpen).OrderBy(c => c.Exam.EndDate)
        }).ToList();
    }
}
