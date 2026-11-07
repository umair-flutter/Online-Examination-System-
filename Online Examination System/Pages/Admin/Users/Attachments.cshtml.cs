using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Admin.Users;

/// <summary>User attachment management (e.g. ID documents, certificates, transcripts).</summary>
public class AttachmentsModel(ApplicationDbContext db, UserManager<ApplicationUser> users, FileStorageService files, AuditService audit) : PageModel
{
    public ApplicationUser AppUser { get; set; } = null!;
    public List<UserAttachment> Items { get; set; } = [];
    [BindProperty] public List<IFormFile> Uploads { get; set; } = [];
    [BindProperty] public string? Description { get; set; }

    private async Task<bool> LoadAsync(string id)
    {
        var u = await users.FindByIdAsync(id);
        if (u is null) return false;
        AppUser = u;
        Items = await db.UserAttachments.Where(a => a.UserId == id).OrderByDescending(a => a.UploadedAt).ToListAsync();
        return true;
    }

    public async Task<IActionResult> OnGetAsync(string id) => await LoadAsync(id) ? Page() : NotFound();

    public async Task<IActionResult> OnPostUploadAsync(string id)
    {
        if (!await LoadAsync(id)) return NotFound();
        if (Uploads.Count == 0) { TempData[Toast.Error] = "Choose at least one file."; return RedirectToPage(new { id }); }
        var errors = Uploads.Select(f => FileStorageService.ValidateAttachment(f) is { } e ? $"{f.FileName}: {e}" : null).Where(e => e != null).ToList();
        if (errors.Count > 0) { TempData[Toast.Error] = string.Join(" ", errors); return RedirectToPage(new { id }); }

        foreach (var f in Uploads)
        {
            db.UserAttachments.Add(new UserAttachment
            {
                UserId = id, FileName = Path.GetFileName(f.FileName), StoredName = await files.SavePrivateAsync(f),
                ContentType = string.IsNullOrEmpty(f.ContentType) ? "application/octet-stream" : f.ContentType,
                Size = f.Length, Description = Description, UploadedBy = User.Identity?.Name
            });
        }
        await db.SaveChangesAsync();
        await audit.LogAsync("UploadAttachment", "User", id, string.Join(", ", Uploads.Select(f => f.FileName)));
        TempData[Toast.Success] = $"{Uploads.Count} file(s) uploaded.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnGetDownloadAsync(string id, int fileId)
    {
        var a = await db.UserAttachments.FirstOrDefaultAsync(x => x.Id == fileId && x.UserId == id);
        var path = a is null ? null : files.GetPrivatePath(a.StoredName);
        return path is null ? NotFound() : PhysicalFile(path, a!.ContentType, a.FileName);
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id, int fileId)
    {
        var a = await db.UserAttachments.FirstOrDefaultAsync(x => x.Id == fileId && x.UserId == id);
        if (a is null) return NotFound();
        files.DeletePrivate(a.StoredName);
        db.UserAttachments.Remove(a);
        await db.SaveChangesAsync();
        await audit.LogAsync("DeleteAttachment", "User", id, a.FileName);
        TempData[Toast.Success] = "Attachment deleted.";
        return RedirectToPage(new { id });
    }
}
