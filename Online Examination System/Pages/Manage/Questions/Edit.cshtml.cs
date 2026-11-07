using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Manage.Questions;

public class EditModel(ApplicationDbContext db, FileStorageService files, GradingService grading, AuditService audit) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public IFormFile? Image { get; set; }
    [BindProperty] public bool RemoveImage { get; set; }
    /// <summary>When creating a question from an exam's page, it is added to that exam afterwards.</summary>
    [BindProperty(SupportsGet = true)] public int? ExamId { get; set; }
    public List<SelectListItem> Subjects { get; set; } = [];
    public string? CurrentImage { get; set; }
    public int AnswerCount { get; set; }

    public class ChoiceInput
    {
        public int Id { get; set; }
        public string? Text { get; set; }
        public bool IsCorrect { get; set; }
    }

    public class InputModel
    {
        public int Id { get; set; }
        [Required, Display(Name = "Subject")] public int? SubjectId { get; set; }
        [Required, Display(Name = "Question")] public string Text { get; set; } = "";
        public QuestionType Type { get; set; } = QuestionType.MultipleChoice;
        [Range(0.25, 1000)] public double Marks { get; set; } = 1;
        public Difficulty Difficulty { get; set; } = Difficulty.Medium;
        [StringLength(2000)] public string? Explanation { get; set; }
        [StringLength(1000), Display(Name = "Accepted answers")] public string? CorrectShortAnswer { get; set; }
        public bool CaseSensitive { get; set; }
        [StringLength(200)] public string? Tags { get; set; }
        public bool TrueIsCorrect { get; set; } = true;
        public List<ChoiceInput> Choices { get; set; } = [];
    }

    private async Task LoadAsync()
    {
        Subjects = await db.Subjects.OrderBy(s => s.Name).Select(s => new SelectListItem($"{s.Code} – {s.Name}", s.Id.ToString())).ToListAsync();
        if (Input.Id > 0) AnswerCount = await db.AttemptAnswers.CountAsync(a => a.QuestionId == Input.Id && a.IsAnswered);
    }

    public async Task<IActionResult> OnGetAsync(int? id, int? subjectId)
    {
        if (id is null)
        {
            Input.SubjectId = subjectId;
            if (ExamId is int eid) Input.SubjectId ??= await db.Exams.Where(e => e.Id == eid).Select(e => (int?)e.SubjectId).FirstOrDefaultAsync();
            Input.Choices = Enumerable.Range(0, 4).Select(i => new ChoiceInput { IsCorrect = i == 0 }).ToList();
            await LoadAsync();
            return Page();
        }
        var q = await db.Questions.Include(x => x.Choices).FirstOrDefaultAsync(x => x.Id == id);
        if (q is null) return NotFound();
        if (!User.IsAdmin() && q.CreatedById != User.UserId()) return Forbid();
        CurrentImage = q.ImagePath;
        Input = new InputModel
        {
            Id = q.Id, SubjectId = q.SubjectId, Text = q.Text, Type = q.Type, Marks = q.Marks, Difficulty = q.Difficulty,
            Explanation = q.Explanation, CorrectShortAnswer = q.CorrectShortAnswer, CaseSensitive = q.CaseSensitive, Tags = q.Tags,
            TrueIsCorrect = q.Type != QuestionType.TrueFalse || q.Choices.FirstOrDefault(c => c.Text == "True")?.IsCorrect == true,
            Choices = q.Choices.OrderBy(c => c.Order).Select(c => new ChoiceInput { Id = c.Id, Text = c.Text, IsCorrect = c.IsCorrect }).ToList()
        };
        if (q.Type is not (QuestionType.MultipleChoice or QuestionType.MultipleAnswer))
            Input.Choices = Enumerable.Range(0, 4).Select(i => new ChoiceInput { IsCorrect = i == 0 }).ToList();
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Question? q = null;
        if (Input.Id > 0)
        {
            q = await db.Questions.Include(x => x.Choices).FirstOrDefaultAsync(x => x.Id == Input.Id);
            if (q is null) return NotFound();
            if (!User.IsAdmin() && q.CreatedById != User.UserId()) return Forbid();
            CurrentImage = q.ImagePath;
        }

        var choices = Input.Choices.Where(c => !string.IsNullOrWhiteSpace(c.Text)).ToList();
        switch (Input.Type)
        {
            case QuestionType.MultipleChoice:
                if (choices.Count < 2) ModelState.AddModelError("", "Add at least two choices.");
                if (choices.Count(c => c.IsCorrect) != 1) ModelState.AddModelError("", "Mark exactly one choice as correct.");
                break;
            case QuestionType.MultipleAnswer:
                if (choices.Count < 2) ModelState.AddModelError("", "Add at least two choices.");
                if (!choices.Any(c => c.IsCorrect)) ModelState.AddModelError("", "Mark at least one choice as correct.");
                break;
            case QuestionType.ShortAnswer:
                if (string.IsNullOrWhiteSpace(Input.CorrectShortAnswer)) ModelState.AddModelError("Input.CorrectShortAnswer", "Enter at least one accepted answer.");
                break;
        }
        if (q != null && q.Type != Input.Type && await db.AttemptAnswers.AnyAsync(a => a.QuestionId == q.Id))
            ModelState.AddModelError("Input.Type", "The type cannot be changed because students have already answered this question.");
        if (Image != null && FileStorageService.ValidateImage(Image) is { } err) ModelState.AddModelError(nameof(Image), err);
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }

        var isNew = q is null;
        string? keyBefore = q is null ? null : AnswerKey(q);
        if (q is null)
        {
            q = new Question { CreatedById = User.UserId() };
            db.Questions.Add(q);
        }
        else q.UpdatedAt = DateTime.Now;

        q.SubjectId = Input.SubjectId!.Value;
        q.Text = Input.Text.Trim();
        q.Type = Input.Type;
        q.Marks = Input.Marks;
        q.Difficulty = Input.Difficulty;
        q.Explanation = Input.Explanation;
        q.Tags = Input.Tags?.Trim();
        q.CaseSensitive = Input.CaseSensitive;
        q.CorrectShortAnswer = Input.Type == QuestionType.ShortAnswer ? Input.CorrectShortAnswer?.Trim() : null;

        // Update choices in place so that choice ids stored in students' answers stay valid.
        var desired = Input.Type switch
        {
            QuestionType.TrueFalse =>
            [
                new ChoiceInput { Id = q.Choices.FirstOrDefault(c => c.Text == "True")?.Id ?? 0, Text = "True", IsCorrect = Input.TrueIsCorrect },
                new ChoiceInput { Id = q.Choices.FirstOrDefault(c => c.Text == "False")?.Id ?? 0, Text = "False", IsCorrect = !Input.TrueIsCorrect }
            ],
            QuestionType.MultipleChoice or QuestionType.MultipleAnswer => choices,
            _ => new List<ChoiceInput>()
        };
        var keep = desired.Where(d => d.Id > 0).Select(d => d.Id).ToHashSet();
        foreach (var c in q.Choices.Where(c => !keep.Contains(c.Id)).ToList()) { q.Choices.Remove(c); db.QuestionChoices.Remove(c); }
        for (var i = 0; i < desired.Count; i++)
        {
            var d = desired[i];
            var c = d.Id > 0 ? q.Choices.FirstOrDefault(x => x.Id == d.Id) : null;
            if (c is null) q.Choices.Add(c = new QuestionChoice());
            c.Text = d.Text!.Trim();
            c.IsCorrect = d.IsCorrect;
            c.Order = i;
        }

        if (RemoveImage || Image != null) { files.DeletePublicFile(q.ImagePath); q.ImagePath = null; }
        if (Image != null) q.ImagePath = await files.SavePublicImageAsync(Image, "questions");

        await db.SaveChangesAsync();

        if (isNew && ExamId is int examId && await db.Exams.AnyAsync(e => e.Id == examId))
        {
            var order = await db.ExamQuestions.Where(e => e.ExamId == examId).MaxAsync(e => (int?)e.Order) ?? -1;
            db.ExamQuestions.Add(new ExamQuestion { ExamId = examId, QuestionId = q.Id, Order = order + 1 });
            await db.SaveChangesAsync();
        }

        // Regrading: if the answer key or marks changed, recalculate every submitted result that contains this question.
        var regraded = 0;
        if (!isNew && keyBefore != AnswerKey(q)) regraded = await grading.RegradeQuestionAsync(q.Id);

        await audit.LogAsync(isNew ? "Create" : "Update", "Question", q.Id, q.Text.Truncate(100) + (regraded > 0 ? $" (regraded {regraded} attempt(s))" : ""));
        TempData[Toast.Success] = regraded > 0 ? $"Question saved. {regraded} result(s) were re-graded automatically." : "Question saved.";
        return ExamId is int ex ? RedirectToPage("/Manage/Exams/Questions", new { id = ex }) : RedirectToPage("Index");
    }

    private static string AnswerKey(Question q) =>
        $"{q.Marks}|{q.CorrectShortAnswer}|{q.CaseSensitive}|{string.Join(',', q.Choices.OrderBy(c => c.Id).Select(c => $"{c.Id}:{c.IsCorrect}"))}";
}
