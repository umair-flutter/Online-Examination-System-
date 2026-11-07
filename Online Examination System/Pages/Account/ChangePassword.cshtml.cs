using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Account;

public class ChangePasswordModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, AuditService audit) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required, DataType(DataType.Password), Display(Name = "Current password")] public string OldPassword { get; set; } = "";
        [Required, StringLength(100, MinimumLength = 6), DataType(DataType.Password), Display(Name = "New password")] public string NewPassword { get; set; } = "";
        [DataType(DataType.Password), Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match."), Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = "";
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var res = await users.ChangePasswordAsync(user, Input.OldPassword, Input.NewPassword);
        if (!res.Succeeded)
        {
            foreach (var e in res.Errors) ModelState.AddModelError("", e.Description);
            return Page();
        }
        await signIn.RefreshSignInAsync(user);
        await audit.LogAsync("ChangePassword", "User", user.Id);
        TempData[Toast.Success] = "Password changed successfully.";
        return RedirectToPage("/Account/Profile");
    }
}
