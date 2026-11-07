using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Admin.Subjects;

public record SubjectRow(int Id, string Code, string Name, string? Description, int Questions, int Exams, DateTime CreatedAt);

public class IndexModel(ApplicationDbContext db, ExcelService excel, PdfService pdf, AuditService audit) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;
    public PagedResult<SubjectRow> Items { get; set; } = null!;

    private IQueryable<SubjectRow> Query()
    {
        var q = db.Subjects.AsQueryable();
        if (!string.IsNullOrWhiteSpace(Search)) q = q.Where(s => s.Name.Contains(Search) || s.Code.Contains(Search));
        return q.OrderBy(s => s.Code).Select(s => new SubjectRow(s.Id, s.Code, s.Name, s.Description, s.Questions.Count,
            db.Exams.Count(e => e.SubjectId == s.Id), s.CreatedAt));
    }

    public async Task OnGetAsync() => Items = await Query().ToPagedAsync(PageNo);

    private static readonly string[] Headers = ["Code", "Name", "Description", "Questions", "Exams", "Created"];
    private static IReadOnlyList<object?> Row(SubjectRow s) => [s.Code, s.Name, s.Description, s.Questions, s.Exams, s.CreatedAt];

    public async Task<IActionResult> OnGetExcelAsync() =>
        File(excel.Export("Subjects", Headers, (await Query().ToListAsync()).Select(Row)), ExcelService.ContentType, "subjects.xlsx");

    public async Task<IActionResult> OnGetPdfAsync() =>
        File(pdf.Table("Subjects", null, Headers, (await Query().ToListAsync()).Select(Row)), PdfService.ContentType, "subjects.pdf");

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var s = await db.Subjects.FindAsync(id);
        if (s is null) return NotFound();
        if (await db.Questions.AnyAsync(q => q.SubjectId == id) || await db.Exams.AnyAsync(e => e.SubjectId == id))
        {
            TempData[Toast.Error] = "This subject is used by questions or exams and cannot be deleted.";
            return RedirectToPage();
        }
        db.Subjects.Remove(s);
        await db.SaveChangesAsync();
        await audit.LogAsync("Delete", "Subject", id, s.Name);
        TempData[Toast.Success] = "Subject deleted.";
        return RedirectToPage();
    }
}
