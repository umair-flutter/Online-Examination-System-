using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Student;

/// <summary>
/// Strict exam player: one question per page, the answer must be saved before moving on,
/// the timer is enforced on the server and the attempt is finalised as soon as the deadline passes.
/// </summary>
public class TakeModel(ApplicationDbContext db, GradingService grading, AuditService audit) : PageModel
{
    public ExamAttempt Attempt { get; set; } = null!;
    public Question Question { get; set; } = null!;
    public AttemptAnswer Answer { get; set; } = null!;
    public List<QuestionChoice> Choices { get; set; } = [];
    public int Index { get; set; }
    public int Total { get; set; }
    public int FirstUnanswered { get; set; }
    public HashSet<int> AnsweredIndexes { get; set; } = [];
    public bool AllAnswered => FirstUnanswered >= Total;
    public int RemainingSeconds { get; set; }
    public bool Review { get; set; }

    private async Task<IActionResult?> LoadAsync(int id)
    {
        var a = await db.ExamAttempts.Include(x => x.Exam).Include(x => x.Answers).FirstOrDefaultAsync(x => x.Id == id);
        if (a is null || a.StudentId != User.UserId()) return NotFound();
        if (a.IsSubmitted) return RedirectToPage("Result", new { id });
        if (DateTime.Now >= a.Deadline)
        {
            await grading.SubmitAsync(a.Id, auto: true);
            TempData[Toast.Error] = "Time is up. Your exam was submitted automatically.";
            return RedirectToPage("Result", new { id });
        }
        Attempt = a;
        var ids = a.QuestionIds;
        Total = ids.Count;
        var answered = a.Answers.Where(x => x.IsAnswered).Select(x => x.QuestionId).ToHashSet();
        AnsweredIndexes = ids.Select((q, i) => (q, i)).Where(x => answered.Contains(x.q)).Select(x => x.i).ToHashSet();
        FirstUnanswered = Enumerable.Range(0, Total).FirstOrDefault(i => !AnsweredIndexes.Contains(i), Total);
        RemainingSeconds = (int)Math.Max(0, (a.Deadline - DateTime.Now).TotalSeconds);
        return null;
    }

    /// <summary>Strict navigation: a question is reachable only if every earlier question is answered.</summary>
    private bool CanOpen(int index) =>
        index >= 0 && index < Total &&
        (Attempt.Exam.AllowBackNavigation ? index <= FirstUnanswered : index == Math.Min(FirstUnanswered, Total - 1));

    private async Task LoadQuestionAsync()
    {
        var qid = Attempt.QuestionIds[Index];
        Question = await db.Questions.Include(q => q.Choices).AsNoTracking().FirstAsync(q => q.Id == qid);
        Answer = Attempt.Answers.First(x => x.QuestionId == qid);
        var order = Answer.ChoiceOrderIds;
        Choices = order.Count > 0
            ? order.Select(cid => Question.Choices.FirstOrDefault(c => c.Id == cid)).OfType<QuestionChoice>().ToList()
            : Question.Choices.OrderBy(c => c.Order).ToList();
    }

    public async Task<IActionResult> OnGetAsync(int id, int? q, bool review = false)
    {
        if (await LoadAsync(id) is { } r) return r;
        Review = review && AllAnswered;
        if (Review) return Page();

        var target = q ?? Math.Min(FirstUnanswered, Total - 1);
        if (!CanOpen(target))
        {
            // Trying to skip ahead (or go back when not allowed) → send the student back to the current question.
            if (q != null) TempData[Toast.Error] = "Please save the answer to the current question first.";
            return RedirectToPage(new { id, q = Math.Min(FirstUnanswered, Total - 1) });
        }
        Index = target;
        await LoadQuestionAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(int id, int index, List<int>? choiceIds, string? textAnswer, string go = "next")
    {
        if (await LoadAsync(id) is { } r) return r;
        if (!CanOpen(index)) return RedirectToPage(new { id });
        Index = index;
        await LoadQuestionAsync();

        var validChoice = Question.Choices.Select(c => c.Id).ToHashSet();
        var picked = (choiceIds ?? []).Where(validChoice.Contains).Distinct().ToList();
        if (Question.Type is QuestionType.MultipleChoice or QuestionType.TrueFalse && picked.Count > 1) picked = picked.Take(1).ToList();
        var text = textAnswer?.Trim();
        var empty = Question.HasChoices ? picked.Count == 0 : string.IsNullOrWhiteSpace(text);

        if (empty)
        {
            if (go == "prev" && Attempt.Exam.AllowBackNavigation && index > 0) return RedirectToPage(new { id, q = index - 1 });
            TempData[Toast.Error] = "Please answer this question before continuing.";
            return RedirectToPage(new { id, q = index });
        }

        var entity = await db.AttemptAnswers.FirstAsync(x => x.Id == Answer.Id);
        entity.SelectedChoiceIds = Question.HasChoices ? string.Join(',', picked) : null;
        entity.TextAnswer = Question.HasChoices ? null : text?.Length > 20000 ? text[..20000] : text;
        entity.IsAnswered = true;
        entity.AnsweredAt = DateTime.Now;
        await db.SaveChangesAsync();

        if (go == "prev" && Attempt.Exam.AllowBackNavigation && index > 0) return RedirectToPage(new { id, q = index - 1 });
        if (go == "stay") return RedirectToPage(new { id, q = index });
        // jump to next question, or to the review/submit screen once everything is answered
        AnsweredIndexes.Add(index);
        var next = Enumerable.Range(0, Total).FirstOrDefault(i => !AnsweredIndexes.Contains(i), Total);
        if (index + 1 < Total && AnsweredIndexes.Contains(index + 1) && Attempt.Exam.AllowBackNavigation) next = index + 1;
        return next >= Total ? RedirectToPage(new { id, review = true }) : RedirectToPage(new { id, q = next });
    }

    public async Task<IActionResult> OnPostSubmitAsync(int id)
    {
        if (await LoadAsync(id) is { } r) return r;
        if (!AllAnswered)
        {
            TempData[Toast.Error] = "Answer every question before submitting.";
            return RedirectToPage(new { id });
        }
        await grading.SubmitAsync(id);
        await audit.LogAsync("SubmitExam", "ExamAttempt", id, Attempt.Exam.Title);
        TempData[Toast.Success] = "Your exam has been submitted.";
        return RedirectToPage("Result", new { id });
    }

    /// <summary>Called by the countdown when it reaches zero. The server verifies the deadline itself.</summary>
    public async Task<IActionResult> OnPostTimeoutAsync(int id)
    {
        var a = await db.ExamAttempts.FirstOrDefaultAsync(x => x.Id == id && x.StudentId == User.UserId());
        if (a is null) return NotFound();
        if (!a.IsSubmitted && DateTime.Now >= a.Deadline.AddSeconds(-3))
        {
            await grading.SubmitAsync(id, auto: true);
            TempData[Toast.Error] = "Time is up. Your exam was submitted automatically.";
        }
        return RedirectToPage(a.IsSubmitted || DateTime.Now >= a.Deadline.AddSeconds(-3) ? "Result" : "Take", new { id });
    }

    /// <summary>Proctoring beacon (tab switch, leaving full screen, copy attempt…). Returns whether the attempt was auto-submitted.</summary>
    public async Task<IActionResult> OnPostViolationAsync(int id, string type, string? details)
    {
        var a = await db.ExamAttempts.Include(x => x.Exam).FirstOrDefaultAsync(x => x.Id == id && x.StudentId == User.UserId());
        if (a is null) return NotFound();
        if (a.IsSubmitted) return new JsonResult(new { submitted = true });
        type = type.Truncate(50);
        db.ProctorEvents.Add(new ProctorEvent { AttemptId = id, EventType = type, Details = details.Truncate(500) });
        var counts = type is "tab-hidden" or "window-blur" or "fullscreen-exit" or "copy" or "paste" or "devtools";
        if (counts) a.ViolationCount++;
        await db.SaveChangesAsync();

        var submit = counts && a.Exam.EnableProctoring && a.Exam.MaxViolations > 0 && a.ViolationCount >= a.Exam.MaxViolations;
        if (submit)
        {
            await grading.SubmitAsync(id, auto: true);
            await audit.LogAsync("AutoSubmitViolations", "ExamAttempt", id, $"{a.ViolationCount} violations");
            TempData[Toast.Error] = "Your exam was submitted automatically because of too many proctoring violations.";
        }
        return new JsonResult(new { submitted = submit, count = a.ViolationCount, max = a.Exam.MaxViolations });
    }
}
