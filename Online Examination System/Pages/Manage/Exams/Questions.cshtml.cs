using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Manage.Exams;

public class QuestionsModel(ApplicationDbContext db, AuditService audit) : PageModel
{
    public Exam Exam { get; set; } = null!;
    public List<ExamQuestion> Current { get; set; } = [];
    public List<Question> Bank { get; set; } = [];
    public List<SelectListItem> Subjects { get; set; } = [];
    public bool HasAttempts { get; set; }

    [BindProperty(SupportsGet = true)] public int? SubjectId { get; set; }
    [BindProperty(SupportsGet = true)] public QuestionType? Type { get; set; }
    [BindProperty(SupportsGet = true)] public Difficulty? Difficulty { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }

    private async Task<IActionResult?> LoadExamAsync(int id)
    {
        var e = await db.Exams.Include(x => x.Subject).FirstOrDefaultAsync(x => x.Id == id);
        if (e is null) return NotFound();
        if (!User.CanManage(e)) return Forbid();
        Exam = e;
        return null;
    }

    private IQueryable<Question> BankQuery(int examId)
    {
        var used = db.ExamQuestions.Where(x => x.ExamId == examId).Select(x => x.QuestionId);
        var q = db.Questions.Where(x => !used.Contains(x.Id));
        if (!User.IsAdmin()) { var uid = User.UserId(); q = q.Where(x => x.CreatedById == uid); }
        if (SubjectId is int s) q = q.Where(x => x.SubjectId == s);
        if (Type is { } t) q = q.Where(x => x.Type == t);
        if (Difficulty is { } d) q = q.Where(x => x.Difficulty == d);
        if (!string.IsNullOrWhiteSpace(Search)) q = q.Where(x => x.Text.Contains(Search) || (x.Tags != null && x.Tags.Contains(Search)));
        return q;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (await LoadExamAsync(id) is { } r) return r;
        if (!Request.Query.ContainsKey("subjectId")) SubjectId = Exam.SubjectId;
        Current = await db.ExamQuestions.Where(x => x.ExamId == id).Include(x => x.Question).ThenInclude(q => q.Choices)
            .OrderBy(x => x.Order).AsNoTracking().ToListAsync();
        Bank = await BankQuery(id).Include(x => x.Subject).OrderByDescending(x => x.Id).Take(100).AsNoTracking().ToListAsync();
        Subjects = await db.Subjects.OrderBy(s => s.Name).Select(s => new SelectListItem(s.Name, s.Id.ToString())).ToListAsync();
        HasAttempts = await db.ExamAttempts.AnyAsync(a => a.ExamId == id);
        return Page();
    }

    private async Task AppendAsync(int examId, IEnumerable<int> questionIds)
    {
        var order = await db.ExamQuestions.Where(x => x.ExamId == examId).MaxAsync(x => (int?)x.Order) ?? -1;
        var existing = await db.ExamQuestions.Where(x => x.ExamId == examId).Select(x => x.QuestionId).ToListAsync();
        foreach (var qid in questionIds.Distinct().Except(existing))
            db.ExamQuestions.Add(new ExamQuestion { ExamId = examId, QuestionId = qid, Order = ++order });
        await db.SaveChangesAsync();
    }

    public async Task<IActionResult> OnPostAddAsync(int id, List<int> questionIds)
    {
        if (await LoadExamAsync(id) is { } r) return r;
        var allowed = await BankQuery(id).Where(q => questionIds.Contains(q.Id)).Select(q => q.Id).ToListAsync();
        await AppendAsync(id, allowed);
        await audit.LogAsync("AddQuestions", "Exam", id, $"{allowed.Count} question(s)");
        TempData[Toast.Success] = $"{allowed.Count} question(s) added.";
        return RedirectToPage(new { id, SubjectId, Type, Difficulty, Search });
    }

    /// <summary>Randomly picks N questions from the bank using the current filters.</summary>
    public async Task<IActionResult> OnPostRandomAsync(int id, int count)
    {
        if (await LoadExamAsync(id) is { } r) return r;
        count = Math.Clamp(count, 1, 200);
        var ids = await BankQuery(id).Select(q => q.Id).ToListAsync();
        var pick = ids.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
        await AppendAsync(id, pick);
        await audit.LogAsync("RandomQuestions", "Exam", id, $"{pick.Count} question(s)");
        TempData[pick.Count > 0 ? Toast.Success : Toast.Error] = pick.Count > 0 ? $"{pick.Count} random question(s) added." : "No matching questions in the bank.";
        return RedirectToPage(new { id, SubjectId, Type, Difficulty, Search });
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id, int examQuestionId)
    {
        if (await LoadExamAsync(id) is { } r) return r;
        var eq = await db.ExamQuestions.FirstOrDefaultAsync(x => x.Id == examQuestionId && x.ExamId == id);
        if (eq != null) { db.ExamQuestions.Remove(eq); await db.SaveChangesAsync(); }
        TempData[Toast.Success] = "Question removed from the exam.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostMoveAsync(int id, int examQuestionId, int dir)
    {
        if (await LoadExamAsync(id) is { } r) return r;
        var list = await db.ExamQuestions.Where(x => x.ExamId == id).OrderBy(x => x.Order).ToListAsync();
        var i = list.FindIndex(x => x.Id == examQuestionId);
        var j = i + Math.Sign(dir);
        if (i >= 0 && j >= 0 && j < list.Count) (list[i], list[j]) = (list[j], list[i]);
        for (var k = 0; k < list.Count; k++) list[k].Order = k;
        await db.SaveChangesAsync();
        return RedirectToPage(new { id });
    }
}
