using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Dashboard;

public record StatCard(string Label, object Value, string Icon, string Color, string? Link = null);
public record ChartSeries(List<string> Labels, List<double> Values);
public record WrongQuestion(int Id, string Text, int Wrong, int Total);

public class IndexModel(ApplicationDbContext db, ExamService exams) : PageModel
{
    public bool IsStaff { get; set; }
    public List<StatCard> Cards { get; set; } = [];
    public ChartSeries AttemptsPerDay { get; set; } = new([], []);
    public ChartSeries PassFail { get; set; } = new([], []);
    public ChartSeries AvgPerExam { get; set; } = new([], []);
    public ChartSeries QuestionTypes { get; set; } = new([], []);
    public List<WrongQuestion> TopWrong { get; set; } = [];
    public List<ExamAttempt> RecentAttempts { get; set; } = [];
    public List<Exam> Upcoming { get; set; } = [];
    public ChartSeries MyScores { get; set; } = new([], []);

    public async Task OnGetAsync()
    {
        var uid = User.UserId();
        IsStaff = User.IsInRole(Roles.Admin) || User.IsInRole(Roles.Teacher);
        if (IsStaff) await LoadStaffAsync(uid, User.IsAdmin());
        else await LoadStudentAsync(uid);
    }

    private async Task LoadStaffAsync(string uid, bool admin)
    {
        var examQ = db.Exams.AsQueryable();
        var questionQ = db.Questions.AsQueryable();
        if (!admin)
        {
            examQ = examQ.Where(e => e.CreatedById == uid);
            questionQ = questionQ.Where(q => q.CreatedById == uid);
        }
        var attemptQ = db.ExamAttempts.Where(a => examQ.Select(e => e.Id).Contains(a.ExamId));
        var submitted = attemptQ.Where(a => a.Status != AttemptStatus.InProgress);

        var studentRole = await db.Roles.Where(r => r.Name == Roles.Student).Select(r => r.Id).FirstAsync();
        var teacherRole = await db.Roles.Where(r => r.Name == Roles.Teacher).Select(r => r.Id).FirstAsync();
        var now = DateTime.Now;

        if (admin)
        {
            Cards.Add(new("Students", await db.UserRoles.CountAsync(r => r.RoleId == studentRole), "bi-people", "primary", "/Admin/Users?role=Student"));
            Cards.Add(new("Teachers", await db.UserRoles.CountAsync(r => r.RoleId == teacherRole), "bi-person-workspace", "info", "/Admin/Users?role=Teacher"));
            Cards.Add(new("Classes", await db.ClassRooms.CountAsync(), "bi-diagram-3", "secondary", "/Admin/Classes"));
            Cards.Add(new("Subjects", await db.Subjects.CountAsync(), "bi-book", "dark", "/Admin/Subjects"));
        }
        Cards.Add(new("Questions", await questionQ.CountAsync(), "bi-collection", "success", "/Manage/Questions"));
        Cards.Add(new("Exams", await examQ.CountAsync(), "bi-file-earmark-text", "warning", "/Manage/Exams"));
        Cards.Add(new("Active exams", await examQ.CountAsync(e => e.Status == ExamStatus.Published && e.StartDate <= now && e.EndDate >= now), "bi-broadcast", "danger", "/Manage/Exams?state=open"));
        Cards.Add(new("Pending marking", await submitted.CountAsync(a => a.Status == AttemptStatus.PendingMarking), "bi-pencil-square", "warning", "/Manage/Marking"));

        // Attempts per day (last 14 days)
        var from = DateTime.Today.AddDays(-13);
        var perDay = await submitted.Where(a => a.SubmittedAt >= from)
            .GroupBy(a => a.SubmittedAt!.Value.Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync();
        AttemptsPerDay = new(
            Enumerable.Range(0, 14).Select(i => from.AddDays(i).ToString("MMM d")).ToList(),
            Enumerable.Range(0, 14).Select(i => (double)(perDay.FirstOrDefault(p => p.Day == from.AddDays(i))?.Count ?? 0)).ToList());

        var graded = submitted.Where(a => a.Status == AttemptStatus.Graded);
        PassFail = new(["Passed", "Failed"], [await graded.CountAsync(a => a.IsPassed), await graded.CountAsync(a => !a.IsPassed)]);

        var avg = await graded.GroupBy(a => a.Exam.Title).Select(g => new { g.Key, Avg = g.Average(a => a.Percentage) })
            .OrderByDescending(x => x.Avg).Take(8).ToListAsync();
        AvgPerExam = new(avg.Select(a => a.Key.Truncate(25)).ToList(), avg.Select(a => Math.Round(a.Avg, 1)).ToList());

        var types = await questionQ.GroupBy(q => q.Type).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        QuestionTypes = new(types.Select(t => t.Key.DisplayName()).ToList(), types.Select(t => (double)t.Count).ToList());

        // Top 5 questions answered wrongly (objective questions only)
        var wrong = await db.AttemptAnswers
            .Where(a => !a.NeedsManualMarking && submitted.Select(s => s.Id).Contains(a.AttemptId))
            .GroupBy(a => a.QuestionId)
            .Select(g => new { QuestionId = g.Key, Wrong = g.Count(x => x.IsCorrect == false), Total = g.Count() })
            .Where(x => x.Wrong > 0)
            .OrderByDescending(x => x.Wrong).Take(5).ToListAsync();
        var wrongIds = wrong.Select(w => w.QuestionId).ToList();
        var texts = await db.Questions.Where(q => wrongIds.Contains(q.Id)).ToDictionaryAsync(q => q.Id, q => q.Text);
        TopWrong = wrong.Select(w => new WrongQuestion(w.QuestionId, texts.GetValueOrDefault(w.QuestionId, ""), w.Wrong, w.Total)).ToList();

        RecentAttempts = await submitted.Include(a => a.Student).Include(a => a.Exam)
            .OrderByDescending(a => a.SubmittedAt).Take(8).AsNoTracking().ToListAsync();
    }

    private async Task LoadStudentAsync(string uid)
    {
        await exams.FinalizeExpiredAttemptsAsync(uid);
        var now = DateTime.Now;
        var visible = exams.VisibleExamsFor(uid);
        var mine = db.ExamAttempts.Where(a => a.StudentId == uid && a.Status != AttemptStatus.InProgress);

        var openCount = await visible.CountAsync(e => e.StartDate <= now && e.EndDate >= now &&
            (e.MaxAttempts == 0 || db.ExamAttempts.Count(a => a.ExamId == e.Id && a.StudentId == uid) < e.MaxAttempts));
        var graded = mine.Where(a => a.Status == AttemptStatus.Graded);
        var avg = await graded.AnyAsync() ? await graded.AverageAsync(a => a.Percentage) : 0;

        Cards.Add(new("Available exams", openCount, "bi-journal-check", "primary", "/Student/Exams"));
        Cards.Add(new("Completed", await mine.CountAsync(), "bi-check2-circle", "success", "/Student/Results"));
        Cards.Add(new("Passed", await graded.CountAsync(a => a.IsPassed), "bi-trophy", "warning", "/Student/Results"));
        Cards.Add(new("Average score", $"{avg:0.#}%", "bi-graph-up", "info"));

        Upcoming = await visible.Include(e => e.Subject).Where(e => e.EndDate >= now)
            .OrderBy(e => e.StartDate).Take(6).AsNoTracking().ToListAsync();

        var scores = await graded.OrderBy(a => a.SubmittedAt).Select(a => new { a.Exam.Title, a.Percentage }).Take(20).ToListAsync();
        MyScores = new(scores.Select(s => s.Title.Truncate(20)).ToList(), scores.Select(s => s.Percentage).ToList());

        RecentAttempts = await mine.Include(a => a.Exam).OrderByDescending(a => a.SubmittedAt).Take(5).AsNoTracking().ToListAsync();
    }
}
