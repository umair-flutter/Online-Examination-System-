using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Admin;

public class AuditLogsModel(ApplicationDbContext db, ExcelService excel, PdfService pdf) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? ActionName { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? To { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;
    public PagedResult<AuditLog> Items { get; set; } = null!;
    public List<string> Actions { get; set; } = [];

    private IQueryable<AuditLog> Query()
    {
        var q = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(Search)) q = q.Where(a => a.UserName!.Contains(Search) || a.Details!.Contains(Search) || a.Entity!.Contains(Search) || a.IpAddress!.Contains(Search));
        if (!string.IsNullOrEmpty(ActionName)) q = q.Where(a => a.Action == ActionName);
        if (From is { } f) q = q.Where(a => a.CreatedAt >= f);
        if (To is { } t) q = q.Where(a => a.CreatedAt < t.AddDays(1));
        return q.OrderByDescending(a => a.Id);
    }

    public async Task OnGetAsync()
    {
        Actions = await db.AuditLogs.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();
        Items = await Query().ToPagedAsync(PageNo, 25);
    }

    private static readonly string[] Headers = ["Time", "User", "Action", "Entity", "Entity ID", "Details", "IP"];
    private static IReadOnlyList<object?> Row(AuditLog a) => [a.CreatedAt, a.UserName, a.Action, a.Entity, a.EntityId, a.Details, a.IpAddress];

    public async Task<IActionResult> OnGetExcelAsync() =>
        File(excel.Export("Audit log", Headers, (await Query().Take(10000).ToListAsync()).Select(Row)), ExcelService.ContentType, "audit-log.xlsx");

    public async Task<IActionResult> OnGetPdfAsync() =>
        File(pdf.Table("Audit log", null, Headers, (await Query().Take(2000).ToListAsync()).Select(Row), landscape: true), PdfService.ContentType, "audit-log.pdf");
}
