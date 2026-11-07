using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Models;

namespace Online_Examination_System.Services;

/// <summary>
/// Grades answers and attempts, and re-grades already submitted attempts when a question's
/// correct answer is changed (the "regrading" feature).
/// </summary>
public class GradingService(ApplicationDbContext db, NotificationService notifications)
{
    /// <summary>Grades one answer in-memory. Essays and manually marked answers are left alone.</summary>
    public static void GradeAnswer(AttemptAnswer answer, Question q, Exam exam)
    {
        if (answer.ManuallyMarked) return;

        if (!answer.IsAnswered)
        {
            answer.IsCorrect = false;
            answer.MarksAwarded = 0;
            answer.NeedsManualMarking = false;
            return;
        }

        switch (q.Type)
        {
            case QuestionType.Essay:
                answer.NeedsManualMarking = true;
                answer.IsCorrect = null;
                answer.MarksAwarded = null;
                return;

            case QuestionType.ShortAnswer:
            {
                var given = (answer.TextAnswer ?? "").Trim();
                var accepted = (q.CorrectShortAnswer ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var cmp = q.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                var ok = accepted.Any(a => string.Equals(Normalize(a), Normalize(given), cmp));
                Apply(answer, ok, q, exam);
                return;
            }

            case QuestionType.MultipleAnswer:
            {
                var correct = q.Choices.Where(c => c.IsCorrect).Select(c => c.Id).ToHashSet();
                var selected = answer.SelectedIds;
                if (selected.SetEquals(correct)) { Apply(answer, true, q, exam); return; }

                if (exam.PartialCreditMultipleAnswer && correct.Count > 0)
                {
                    var right = selected.Count(correct.Contains);
                    var wrong = selected.Count - right;
                    var fraction = Math.Max(0, (double)(right - wrong) / correct.Count);
                    answer.IsCorrect = false;
                    answer.MarksAwarded = Math.Round(q.Marks * fraction, 2);
                    answer.NeedsManualMarking = false;
                    return;
                }
                Apply(answer, false, q, exam);
                return;
            }

            default: // MultipleChoice, TrueFalse
            {
                var correct = q.Choices.Where(c => c.IsCorrect).Select(c => c.Id).ToHashSet();
                Apply(answer, answer.SelectedIds.SetEquals(correct), q, exam);
                return;
            }
        }
    }

    private static string Normalize(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static void Apply(AttemptAnswer a, bool correct, Question q, Exam exam)
    {
        a.IsCorrect = correct;
        a.NeedsManualMarking = false;
        a.MarksAwarded = correct ? q.Marks : -Math.Round(q.Marks * Math.Clamp(exam.NegativeMarking, 0, 1), 2);
    }

    /// <summary>Recomputes totals, status and pass/fail for an attempt whose answers are loaded.</summary>
    public static void RecalculateTotals(ExamAttempt attempt, Exam exam, IReadOnlyDictionary<int, Question> questions)
    {
        attempt.TotalMarks = attempt.QuestionIds.Where(questions.ContainsKey).Sum(id => questions[id].Marks);
        var score = attempt.Answers.Sum(a => a.MarksAwarded ?? 0);
        attempt.Score = Math.Round(Math.Max(0, score), 2);
        attempt.Percentage = attempt.TotalMarks > 0 ? Math.Round(attempt.Score / attempt.TotalMarks * 100, 2) : 0;
        attempt.Status = attempt.Answers.Any(a => a.NeedsManualMarking && !a.ManuallyMarked)
            ? AttemptStatus.PendingMarking
            : AttemptStatus.Graded;
        attempt.IsPassed = attempt.Status == AttemptStatus.Graded && attempt.Percentage >= exam.PassPercentage;
        if (attempt.IsPassed && string.IsNullOrEmpty(attempt.CertificateCode))
            attempt.CertificateCode = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        if (!attempt.IsPassed) attempt.CertificateCode = null;
    }

    private async Task<ExamAttempt?> LoadAttemptAsync(int attemptId) =>
        await db.ExamAttempts
            .Include(a => a.Exam)
            .Include(a => a.Answers)
            .FirstOrDefaultAsync(a => a.Id == attemptId);

    private async Task<Dictionary<int, Question>> LoadQuestionsAsync(IEnumerable<int> ids)
    {
        var list = ids.Distinct().ToList();
        return await db.Questions.Include(q => q.Choices).Where(q => list.Contains(q.Id)).ToDictionaryAsync(q => q.Id);
    }

    /// <summary>Submits (finalises) an in-progress attempt: creates blank answers for unanswered questions and grades everything.</summary>
    public async Task SubmitAsync(int attemptId, bool auto = false)
    {
        var attempt = await LoadAttemptAsync(attemptId);
        if (attempt is null || attempt.IsSubmitted) return;

        var questions = await LoadQuestionsAsync(attempt.QuestionIds);
        foreach (var qid in attempt.QuestionIds.Where(id => attempt.Answers.All(a => a.QuestionId != id)))
            attempt.Answers.Add(new AttemptAnswer { QuestionId = qid, IsAnswered = false });

        foreach (var ans in attempt.Answers)
            if (questions.TryGetValue(ans.QuestionId, out var q)) GradeAnswer(ans, q, attempt.Exam);

        var now = DateTime.Now;
        attempt.SubmittedAt = now > attempt.Deadline ? attempt.Deadline : now;
        attempt.AutoSubmitted = auto;
        RecalculateTotals(attempt, attempt.Exam, questions);
        await db.SaveChangesAsync();

        if (attempt.Status == AttemptStatus.PendingMarking)
            await notifications.NotifyStaffOfPendingMarkingAsync(attempt.Exam);
    }

    /// <summary>Re-grades every submitted answer of a question after its correct answer changed. Returns number of attempts updated.</summary>
    public async Task<int> RegradeQuestionAsync(int questionId)
    {
        var attemptIds = await db.AttemptAnswers
            .Where(a => a.QuestionId == questionId && a.Attempt.Status != AttemptStatus.InProgress)
            .Select(a => a.AttemptId).Distinct().ToListAsync();
        foreach (var id in attemptIds) await RegradeAttemptAsync(id, notify: true);
        return attemptIds.Count;
    }

    public async Task<int> RegradeExamAsync(int examId)
    {
        var ids = await db.ExamAttempts.Where(a => a.ExamId == examId && a.Status != AttemptStatus.InProgress).Select(a => a.Id).ToListAsync();
        foreach (var id in ids) await RegradeAttemptAsync(id, notify: false);
        return ids.Count;
    }

    public async Task RegradeAttemptAsync(int attemptId, bool notify)
    {
        var attempt = await LoadAttemptAsync(attemptId);
        if (attempt is null || !attempt.IsSubmitted) return;
        var questions = await LoadQuestionsAsync(attempt.QuestionIds);
        var before = attempt.Score;
        foreach (var ans in attempt.Answers)
            if (questions.TryGetValue(ans.QuestionId, out var q)) GradeAnswer(ans, q, attempt.Exam);
        RecalculateTotals(attempt, attempt.Exam, questions);
        await db.SaveChangesAsync();

        if (notify && Math.Abs(before - attempt.Score) > 0.001)
            await notifications.SendAsync(attempt.StudentId, "Result updated",
                $"Your result for \"{attempt.Exam.Title}\" was re-graded: {before} → {attempt.Score} / {attempt.TotalMarks}.",
                $"/Student/Result/{attempt.Id}");
    }

    /// <summary>Saves manual marks for an attempt (essays or overrides) and recomputes the total.</summary>
    public async Task SaveManualMarksAsync(int attemptId, IDictionary<int, (double marks, string? feedback)> marks, string markerId)
    {
        var attempt = await LoadAttemptAsync(attemptId);
        if (attempt is null || !attempt.IsSubmitted) return;
        var questions = await LoadQuestionsAsync(attempt.QuestionIds);
        var wasPending = attempt.Status == AttemptStatus.PendingMarking;

        foreach (var ans in attempt.Answers)
        {
            if (!marks.TryGetValue(ans.Id, out var m) || !questions.TryGetValue(ans.QuestionId, out var q)) continue;
            ans.MarksAwarded = Math.Clamp(m.marks, -q.Marks, q.Marks);
            ans.Feedback = m.feedback;
            ans.ManuallyMarked = true;
            ans.IsCorrect = ans.MarksAwarded >= q.Marks;
            ans.MarkedById = markerId;
            ans.MarkedAt = DateTime.Now;
        }
        RecalculateTotals(attempt, attempt.Exam, questions);
        await db.SaveChangesAsync();

        if (wasPending && attempt.Status == AttemptStatus.Graded)
            await notifications.SendAsync(attempt.StudentId, "Exam graded",
                $"Your exam \"{attempt.Exam.Title}\" has been marked. Score: {attempt.Percentage}%.",
                $"/Student/Result/{attempt.Id}");
    }
}
