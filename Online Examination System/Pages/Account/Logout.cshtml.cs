using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Account;

public class LogoutModel(SignInManager<ApplicationUser> signIn, AuditService audit) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        if (User.Identity?.IsAuthenticated == true) await audit.LogAsync("Logout");
        await signIn.SignOutAsync();
        return RedirectToPage("/Account/Login");
    }
}
