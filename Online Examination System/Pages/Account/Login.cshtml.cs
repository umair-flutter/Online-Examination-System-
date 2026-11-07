using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Account;

[AllowAnonymous]
public class LoginModel(SignInManager<ApplicationUser> signIn, UserManager<ApplicationUser> users, AuditService audit) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required, Display(Name = "Username or email")] public string Login { get; set; } = "";
        [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
        [Display(Name = "Remember me")] public bool RememberMe { get; set; }
    }

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToPage("/Dashboard/Index");
        ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        if (!ModelState.IsValid) return Page();

        var user = Input.Login.Contains('@') ? await users.FindByEmailAsync(Input.Login) : await users.FindByNameAsync(Input.Login);
        if (user is null)
        {
            ModelState.AddModelError("", "Invalid username or password.");
            return Page();
        }
        if (user.Status != UserStatus.Active)
        {
            ModelState.AddModelError("", $"Your account is {user.Status.ToString().ToLower()}. Please contact the administrator.");
            await audit.LogAsync("LoginBlocked", "User", user.Id, $"Status: {user.Status}", user.UserName);
            return Page();
        }

        var result = await signIn.PasswordSignInAsync(user, Input.Password, Input.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            user.LastLoginAt = DateTime.Now;
            await users.UpdateAsync(user);
            await audit.LogAsync("Login", "User", user.Id, userName: user.UserName);
            if (!string.IsNullOrEmpty(user.PreferredLanguage))
                Response.Cookies.Append(CookieRequestCultureProvider.DefaultCookieName,
                    CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(user.PreferredLanguage)),
                    new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, HttpOnly = true });
            return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/Dashboard");
        }
        if (result.IsLockedOut)
        {
            await audit.LogAsync("LockedOut", "User", user.Id, userName: user.UserName);
            ModelState.AddModelError("", "Too many failed attempts. The account is locked for 10 minutes.");
            return Page();
        }
        await audit.LogAsync("LoginFailed", "User", user.Id, userName: user.UserName);
        ModelState.AddModelError("", "Invalid username or password.");
        return Page();
    }
}
