using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Account;

public class ProfileModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, ApplicationDbContext db,
    FileStorageService files, AuditService audit) : PageModel
{
    public ApplicationUser AppUser { get; set; } = null!;
    public IList<string> UserRoles { get; set; } = [];
    public List<string> ClassNames { get; set; } = [];
    public List<UserAttachment> Attachments { get; set; } = [];

    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public IFormFile? Picture { get; set; }

    public class InputModel
    {
        [Required, StringLength(150), Display(Name = "Full name")] public string FullName { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Phone, Display(Name = "Phone")] public string? PhoneNumber { get; set; }
        [StringLength(30), Display(Name = "Student number")] public string? StudentNumber { get; set; }
    }

    private async Task<bool> LoadAsync()
    {
        var u = await users.GetUserAsync(User);
        if (u is null) return false;
        AppUser = u;
        UserRoles = await users.GetRolesAsync(u);
        ClassNames = await db.ClassStudents.Where(c => c.StudentId == u.Id).Select(c => c.ClassRoom.Name).ToListAsync();
        Attachments = await db.UserAttachments.Where(a => a.UserId == u.Id).OrderByDescending(a => a.UploadedAt).ToListAsync();
        return true;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync()) return Challenge();
        Input = new InputModel { FullName = AppUser.FullName, Email = AppUser.Email ?? "", PhoneNumber = AppUser.PhoneNumber, StudentNumber = AppUser.StudentNumber };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await LoadAsync()) return Challenge();
        if (Picture != null && FileStorageService.ValidateImage(Picture) is { } err) ModelState.AddModelError(nameof(Picture), err);
        if (!ModelState.IsValid) return Page();

        if (!string.Equals(AppUser.Email, Input.Email, StringComparison.OrdinalIgnoreCase))
        {
            var res = await users.SetEmailAsync(AppUser, Input.Email);
            if (!res.Succeeded) { foreach (var e in res.Errors) ModelState.AddModelError("", e.Description); return Page(); }
        }
        AppUser.FullName = Input.FullName.Trim();
        AppUser.PhoneNumber = Input.PhoneNumber;
        AppUser.StudentNumber = Input.StudentNumber;
        if (Picture != null)
        {
            files.DeletePublicFile(AppUser.ProfilePicture);
            AppUser.ProfilePicture = await files.SavePublicImageAsync(Picture, "avatars");
        }
        await users.UpdateAsync(AppUser);
        await signIn.RefreshSignInAsync(AppUser);
        await audit.LogAsync("UpdateProfile", "User", AppUser.Id);
        TempData[Toast.Success] = "Profile updated.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemovePictureAsync()
    {
        if (!await LoadAsync()) return Challenge();
        files.DeletePublicFile(AppUser.ProfilePicture);
        AppUser.ProfilePicture = null;
        await users.UpdateAsync(AppUser);
        TempData[Toast.Success] = "Profile picture removed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetDownloadAsync(int id)
    {
        var uid = User.UserId();
        var a = await db.UserAttachments.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid);
        var path = a is null ? null : files.GetPrivatePath(a.StoredName);
        return path is null ? NotFound() : PhysicalFile(path, a!.ContentType, a.FileName);
    }
}
