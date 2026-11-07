using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Models;

namespace Online_Examination_System.Services;

public enum ExamAvailability { NotStarted, Open, Closed, NoAttemptsLeft, InProgress }

public class ExamService(ApplicationDbContext db, GradingService grading)
{
    /// <summary>Published exams that the student may see (exam assigned to one of the student's classes, or to no class at all).</summary>
    public IQueryable<Exam> VisibleExamsFor(string studentId)
    {
        var classIds = db.ClassStudents.Where(c => c.StudentId == studentId).Select(c => c.ClassRoomId);
        return db.Exams.Where(e => e.Status == ExamStatus.Published &&
                                   (!e.Classes.Any() || e.Classes.Any(c => classIds.Contains(c.ClassRoomId))));
    }

    public static ExamAvailability GetAvailability(Exam exam, IReadOnlyCollection<ExamAttempt> myAttempts, DateTime now)
    {
        if (myAttempts.Any(a => a.Status == AttemptStatus.InProgress && a.Deadline > now)) return ExamAvailability.InProgress;
        if (now < exam.StartDate) return ExamAvailability.NotStarted;
        if (now > exam.EndDate) return ExamAvailability.Closed;
        if (exam.MaxAttempts > 0 && myAttempts.Count >= exam.MaxAttempts) return ExamAvailability.NoAttemptsLeft;
        return ExamAvailability.Open;
    }

    /// <summary>Finalises every attempt whose time is over. Called by the background service and before showing student pages.</summary>
    public async Task<int> FinalizeExpiredAttemptsAsync(string? studentId = null)
    {
        var now = DateTime.Now;
        var q = db.ExamAttempts.Where(a => a.Status == AttemptStatus.InProgress && a.Deadline <= now);
        if (studentId != null) q = q.Where(a => a.StudentId == studentId);
        var ids = await q.Select(a => a.Id).ToListAsync();
        foreach (var id in ids) await grading.SubmitAsync(id, auto: true);
        return ids.Count;
    }

    public async Task<ExamAttempt> StartAttemptAsync(Exam exam, string studentId, string? ip)
    {
        var questionIds = await db.ExamQuestions.Where(x => x.ExamId == exam.Id).OrderBy(x => x.Order).Select(x => x.QuestionId).ToListAsync();
        if (exam.RandomizeQuestions) questionIds = questionIds.OrderBy(_ => Random.Shared.Next()).ToList();

        var count = await db.ExamAttempts.CountAsync(a => a.ExamId == exam.Id && a.StudentId == studentId);
        var now = DateTime.Now;
        var attempt = new ExamAttempt
        {
            ExamId = exam.Id,
            StudentId = studentId,
            AttemptNumber = count + 1,
            StartedAt = now,
            Deadline = now.AddMinutes(exam.DurationMinutes),
            QuestionOrder = string.Join(',', questionIds),
            IpAddress = ip
        };

        var choices = await db.QuestionChoices.Where(c => questionIds.Contains(c.QuestionId))
            .OrderBy(c => c.Order).ThenBy(c => c.Id)
            .Select(c => new { c.Id, c.QuestionId }).ToListAsync();
        foreach (var qid in questionIds)
        {
            var ids = choices.Where(c => c.QuestionId == qid).Select(c => c.Id).ToList();
            if (exam.RandomizeChoices) ids = ids.OrderBy(_ => Random.Shared.Next()).ToList();
            attempt.Answers.Add(new AttemptAnswer { QuestionId = qid, ChoiceOrder = string.Join(',', ids) });
        }

        db.ExamAttempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt;
    }
}

/// <summary>Auto-submits attempts whose timer has run out even if the student closed the browser.</summary>
public class ExpiredAttemptsWorker(IServiceScopeFactory scopes, ILogger<ExpiredAttemptsWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<ExamService>();
                var n = await svc.FinalizeExpiredAttemptsAsync();
                if (n > 0) log.LogInformation("Auto-submitted {Count} expired exam attempt(s)", n);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogWarning(ex, "Failed to finalise expired attempts");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
