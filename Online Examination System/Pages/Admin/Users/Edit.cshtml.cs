using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Admin.Users;

public class EditModel(ApplicationDbContext db, UserManager<ApplicationUser> users, FileStorageService files, AuditService audit) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public IFormFile? Picture { get; set; }
    public List<SelectListItem> ClassOptions { get; set; } = [];
    public string? CurrentPicture { get; set; }
    public bool IsNew => string.IsNullOrEmpty(Input.Id);

    public class InputModel
    {
        public string? Id { get; set; }
        [Required, StringLength(150), Display(Name = "Full name")] public string FullName { get; set; } = "";
        [Required, StringLength(50, MinimumLength = 3), RegularExpression(@"^[a-zA-Z0-9._@-]+$")] public string UserName { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Phone] public string? PhoneNumber { get; set; }
        [StringLength(30)] public string? StudentNumber { get; set; }
        [Required] public string Role { get; set; } = Roles.Student;
        public UserStatus Status { get; set; } = UserStatus.Active;
        [StringLength(100, MinimumLength = 6), DataType(DataType.Password)] public string? Password { get; set; }
        public List<int> ClassIds { get; set; } = [];
    }

    private async Task LoadAsync() =>
        ClassOptions = await db.ClassRooms.OrderBy(c => c.Name).Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync();

    public async Task<IActionResult> OnGetAsync(string? id)
    {
        await LoadAsync();
        if (id is null) return Page();
        var u = await users.FindByIdAsync(id);
        if (u is null) return NotFound();
        CurrentPicture = u.ProfilePicture;
        Input = new InputModel
        {
            Id = u.Id, FullName = u.FullName, UserName = u.UserName!, Email = u.Email!, PhoneNumber = u.PhoneNumber, StudentNumber = u.StudentNumber,
            Status = u.Status, Role = (await users.GetRolesAsync(u)).FirstOrDefault() ?? Roles.Student,
            ClassIds = await db.ClassStudents.Where(c => c.StudentId == u.Id).Select(c => c.ClassRoomId).ToListAsync()
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!Roles.All.Contains(Input.Role)) ModelState.AddModelError("Input.Role", "Invalid role.");
        if (IsNew && string.IsNullOrEmpty(Input.Password)) ModelState.AddModelError("Input.Password", "Password is required for new users.");
        if (Picture != null && FileStorageService.ValidateImage(Picture) is { } err) ModelState.AddModelError(nameof(Picture), err);
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }

        ApplicationUser? u;
        IdentityResult res;
        if (IsNew)
        {
            u = new ApplicationUser { EmailConfirmed = true };
            Apply(u);
            res = await users.CreateAsync(u, Input.Password!);
        }
        else
        {
            u = await users.FindByIdAsync(Input.Id!);
            if (u is null) return NotFound();
            if (u.Id == User.UserId() && (Input.Status != UserStatus.Active || Input.Role != Roles.Admin))
            {
                ModelState.AddModelError("", "You cannot deactivate or change the role of your own account.");
                await LoadAsync();
                return Page();
            }
            var statusChanged = u.Status != Input.Status;
            Apply(u);
            res = await users.UpdateAsync(u);
            if (res.Succeeded && !string.IsNullOrEmpty(Input.Password))
            {
                var token = await users.GeneratePasswordResetTokenAsync(u);
                res = await users.ResetPasswordAsync(u, token, Input.Password);
            }
            if (res.Succeeded && statusChanged) await users.UpdateSecurityStampAsync(u);
        }
        if (!res.Succeeded)
        {
            foreach (var e in res.Errors) ModelState.AddModelError("", e.Description);
            await LoadAsync();
            return Page();
        }

        var current = await users.GetRolesAsync(u);
        if (!current.SequenceEqual([Input.Role]))
        {
            await users.RemoveFromRolesAsync(u, current);
            await users.AddToRoleAsync(u, Input.Role);
        }

        if (Picture != null)
        {
            files.DeletePublicFile(u.ProfilePicture);
            u.ProfilePicture = await files.SavePublicImageAsync(Picture, "avatars");
            await users.UpdateAsync(u);
        }

        // class membership (students only)
        var existing = await db.ClassStudents.Where(c => c.StudentId == u.Id).ToListAsync();
        var wanted = Input.Role == Roles.Student ? Input.ClassIds.Distinct().ToHashSet() : [];
        db.ClassStudents.RemoveRange(existing.Where(e => !wanted.Contains(e.ClassRoomId)));
        db.ClassStudents.AddRange(wanted.Where(id => existing.All(e => e.ClassRoomId != id)).Select(id => new ClassStudent { ClassRoomId = id, StudentId = u.Id }));
        await db.SaveChangesAsync();

        await audit.LogAsync(IsNew ? "Create" : "Update", "User", u.Id, $"{u.UserName} ({Input.Role}, {Input.Status})");
        TempData[Toast.Success] = "User saved.";
        return RedirectToPage("Index");
    }

    private void Apply(ApplicationUser u)
    {
        u.FullName = Input.FullName.Trim();
        u.UserName = Input.UserName.Trim();
        u.Email = Input.Email.Trim();
        u.PhoneNumber = Input.PhoneNumber;
        u.StudentNumber = Input.StudentNumber;
        u.Status = Input.Status;
    }
}
