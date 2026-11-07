using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Admin.Users;

public record UserRow(string Id, string FullName, string UserName, string? Email, string? StudentNumber, string? ProfilePicture,
    UserStatus Status, string Role, DateTime CreatedAt, DateTime? LastLoginAt, int Attachments);

public class IndexModel(ApplicationDbContext db, UserManager<ApplicationUser> users, ExcelService excel, PdfService pdf,
    AuditService audit, FileStorageService files) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Role { get; set; }
    [BindProperty(SupportsGet = true)] public UserStatus? Status { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;
    public PagedResult<UserRow> Items { get; set; } = null!;

    private IQueryable<UserRow> Query()
    {
        var q = db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(Search))
            q = q.Where(u => u.FullName.Contains(Search) || u.UserName!.Contains(Search) || u.Email!.Contains(Search) || (u.StudentNumber != null && u.StudentNumber.Contains(Search)));
        if (Status is { } st) q = q.Where(u => u.Status == st);
        if (!string.IsNullOrEmpty(Role))
        {
            var roleId = db.Roles.Where(r => r.Name == Role).Select(r => r.Id);
            q = q.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && roleId.Contains(ur.RoleId)));
        }
        return q.OrderBy(u => u.FullName).Select(u => new UserRow(u.Id, u.FullName, u.UserName!, u.Email, u.StudentNumber, u.ProfilePicture, u.Status,
            db.UserRoles.Where(ur => ur.UserId == u.Id).Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name).FirstOrDefault() ?? "",
            u.CreatedAt, u.LastLoginAt, u.Attachments.Count));
    }

    public async Task OnGetAsync() => Items = await Query().ToPagedAsync(PageNo, 15);

    private static readonly string[] Headers = ["Full name", "Username", "Email", "Role", "Student no.", "Status", "Created", "Last login"];
    private static IReadOnlyList<object?> Row(UserRow u) => [u.FullName, u.UserName, u.Email, u.Role, u.StudentNumber, u.Status, u.CreatedAt, u.LastLoginAt];

    public async Task<IActionResult> OnGetExcelAsync() =>
        File(excel.Export("Users", Headers, (await Query().ToListAsync()).Select(Row)), ExcelService.ContentType, "users.xlsx");

    public async Task<IActionResult> OnGetPdfAsync() =>
        File(pdf.Table("Users", null, Headers, (await Query().ToListAsync()).Select(Row), landscape: true), PdfService.ContentType, "users.pdf");

    public async Task<IActionResult> OnPostStatusAsync(string id, [FromQuery(Name = "newStatus")] UserStatus status)
    {
        var u = await users.FindByIdAsync(id);
        if (u is null) return NotFound();
        if (u.Id == User.UserId()) { TempData[Toast.Error] = "You cannot change your own status."; return RedirectToPage(); }
        u.Status = status;
        await users.UpdateAsync(u);
        await users.UpdateSecurityStampAsync(u); // signs the user out of existing sessions
        await audit.LogAsync("ChangeStatus", "User", u.Id, $"{u.UserName} → {status}");
        TempData[Toast.Success] = $"{u.FullName} is now {status}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id)
    {
        var u = await users.FindByIdAsync(id);
        if (u is null) return NotFound();
        if (u.Id == User.UserId()) { TempData[Toast.Error] = "You cannot delete your own account."; return RedirectToPage(); }
        if (await db.ExamAttempts.AnyAsync(a => a.StudentId == id) || await db.Questions.AnyAsync(q => q.CreatedById == id) || await db.Exams.AnyAsync(e => e.CreatedById == id))
        {
            TempData[Toast.Error] = "This user has exam history or owns questions/exams. Set the status to Inactive instead of deleting.";
            return RedirectToPage();
        }
        foreach (var a in await db.UserAttachments.Where(a => a.UserId == id).ToListAsync()) files.DeletePrivate(a.StoredName);
        files.DeletePublicFile(u.ProfilePicture);
        await users.DeleteAsync(u);
        await audit.LogAsync("Delete", "User", id, u.UserName);
        TempData[Toast.Success] = "User deleted.";
        return RedirectToPage();
    }
}
