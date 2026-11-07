using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Admin.Classes;

public record ClassRow(int Id, string Name, string? Description, int Students, int Exams, DateTime CreatedAt);

public class IndexModel(ApplicationDbContext db, ExcelService excel, PdfService pdf, AuditService audit) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;
    public PagedResult<ClassRow> Items { get; set; } = null!;

    private IQueryable<ClassRow> Query()
    {
        var q = db.ClassRooms.AsQueryable();
        if (!string.IsNullOrWhiteSpace(Search)) q = q.Where(c => c.Name.Contains(Search));
        return q.OrderBy(c => c.Name).Select(c => new ClassRow(c.Id, c.Name, c.Description, c.Students.Count, c.Exams.Count, c.CreatedAt));
    }

    public async Task OnGetAsync() => Items = await Query().ToPagedAsync(PageNo);

    private static readonly string[] Headers = ["Name", "Description", "Students", "Exams", "Created"];
    private static IReadOnlyList<object?> Row(ClassRow c) => [c.Name, c.Description, c.Students, c.Exams, c.CreatedAt];

    public async Task<IActionResult> OnGetExcelAsync() =>
        File(excel.Export("Classes", Headers, (await Query().ToListAsync()).Select(Row)), ExcelService.ContentType, "classes.xlsx");

    public async Task<IActionResult> OnGetPdfAsync() =>
        File(pdf.Table("Classes", null, Headers, (await Query().ToListAsync()).Select(Row)), PdfService.ContentType, "classes.pdf");

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var c = await db.ClassRooms.FindAsync(id);
        if (c is null) return NotFound();
        db.ClassRooms.Remove(c);
        await db.SaveChangesAsync();
        await audit.LogAsync("Delete", "ClassRoom", id, c.Name);
        TempData[Toast.Success] = "Class deleted.";
        return RedirectToPage();
    }
}
