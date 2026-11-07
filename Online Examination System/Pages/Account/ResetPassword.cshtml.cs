using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Account;

[AllowAnonymous]
public class ResetPasswordModel(UserManager<ApplicationUser> users, AuditService audit) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required] public string Code { get; set; } = "";
        [Required, StringLength(100, MinimumLength = 6), DataType(DataType.Password)] public string Password { get; set; } = "";
        [DataType(DataType.Password), Compare(nameof(Password), ErrorMessage = "Passwords do not match.")] public string ConfirmPassword { get; set; } = "";
    }

    public IActionResult OnGet(string? code, string? email)
    {
        if (string.IsNullOrEmpty(code)) return BadRequest("A reset code is required.");
        Input = new InputModel { Code = code, Email = email ?? "" };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var user = await users.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            ModelState.AddModelError("", "Invalid or expired reset link.");
            return Page();
        }
        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Input.Code)); }
        catch (FormatException) { ModelState.AddModelError("", "Invalid or expired reset link."); return Page(); }

        var res = await users.ResetPasswordAsync(user, token, Input.Password);
        if (!res.Succeeded)
        {
            foreach (var e in res.Errors) ModelState.AddModelError("", e.Description);
            return Page();
        }
        await users.SetLockoutEndDateAsync(user, null);
        await audit.LogAsync("ResetPassword", "User", user.Id, userName: user.UserName);
        TempData[Toast.Success] = "Your password has been reset. You can now log in.";
        return RedirectToPage("/Account/Login");
    }
}
