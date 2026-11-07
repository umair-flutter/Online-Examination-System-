using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Account;

/// <summary>Public sign up. New accounts are always students; admins create teachers.</summary>
[AllowAnonymous]
public class RegisterModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, ApplicationDbContext db, AuditService audit) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public List<SelectListItem> ClassOptions { get; set; } = [];

    public class InputModel
    {
        [Required, StringLength(150), Display(Name = "Full name")] public string FullName { get; set; } = "";
        [Required, StringLength(50, MinimumLength = 3), RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Only letters, numbers, dot, dash and underscore.")]
        [Display(Name = "Username")] public string UserName { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Display(Name = "Student number")] public string? StudentNumber { get; set; }
        [Display(Name = "Class")] public int? ClassRoomId { get; set; }
        [Required, StringLength(100, MinimumLength = 6), DataType(DataType.Password)] public string Password { get; set; } = "";
        [DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "Passwords do not match."), Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToPage("/Dashboard/Index");
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync() =>
        ClassOptions = await db.ClassRooms.OrderBy(c => c.Name).Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) { await LoadAsync(); return Page(); }
        var user = new ApplicationUser
        {
            UserName = Input.UserName.Trim(), Email = Input.Email.Trim(), FullName = Input.FullName.Trim(),
            StudentNumber = Input.StudentNumber?.Trim(), EmailConfirmed = true
        };
        var res = await users.CreateAsync(user, Input.Password);
        if (!res.Succeeded)
        {
            foreach (var e in res.Errors) ModelState.AddModelError("", e.Description);
            await LoadAsync();
            return Page();
        }
        await users.AddToRoleAsync(user, Roles.Student);
        if (Input.ClassRoomId is int cid && await db.ClassRooms.AnyAsync(c => c.Id == cid))
        {
            db.ClassStudents.Add(new ClassStudent { ClassRoomId = cid, StudentId = user.Id });
            await db.SaveChangesAsync();
        }
        await audit.LogAsync("Register", "User", user.Id, userName: user.UserName);
        await signIn.SignInAsync(user, isPersistent: false);
        TempData[Toast.Success] = "Welcome! Your account has been created.";
        return RedirectToPage("/Dashboard/Index");
    }
}
