using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Manage.Exams;

/// <summary>Staff view of one student's attempt, including proctoring events.</summary>
public class AttemptModel(ApplicationDbContext db, PdfService pdf) : PageModel
{
    public ExamAttempt Attempt { get; set; } = null!;
    public Dictionary<int, Question> Questions { get; set; } = [];
    public List<ProctorEvent> Events { get; set; } = [];

    private async Task<IActionResult?> LoadAsync(int id)
    {
        var a = await db.ExamAttempts.Include(x => x.Exam).ThenInclude(e => e.Subject).Include(x => x.Student).Include(x => x.Answers)
            .AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (a is null) return NotFound();
        if (!User.CanManage(a.Exam)) return Forbid();
        Attempt = a;
        var ids = a.QuestionIds;
        Questions = await db.Questions.Include(q => q.Choices).Where(q => ids.Contains(q.Id)).AsNoTracking().ToDictionaryAsync(q => q.Id);
        return null;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (await LoadAsync(id) is { } r) return r;
        Events = await db.ProctorEvents.Where(e => e.AttemptId == id).OrderBy(e => e.OccurredAt).AsNoTracking().ToListAsync();
        return Page();
    }

    public async Task<IActionResult> OnGetPdfAsync(int id)
    {
        if (await LoadAsync(id) is { } r) return r;
        return File(pdf.ResultSlip(Attempt, Questions, includeAnswers: true), PdfService.ContentType, $"attempt-{id}.pdf");
    }
}
