using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Account;

[AllowAnonymous]
public class ForgotPasswordModel(UserManager<ApplicationUser> users, IAppEmailSender email, IWebHostEnvironment env, AuditService audit) : PageModel
{
    [BindProperty, Required, EmailAddress] public string Email { get; set; } = "";
    public bool Sent { get; set; }
    /// <summary>Only shown in Development when SMTP is not configured, so the flow can be tested locally.</summary>
    public string? DevLink { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        Sent = true;
        var user = await users.FindByEmailAsync(Email);
        if (user is null || user.Status != UserStatus.Active) return Page(); // don't reveal whether the account exists

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var link = Url.Page("/Account/ResetPassword", null, new { email = user.Email, code }, Request.Scheme)!;
        var delivered = await email.SendAsync(user.Email!, "Reset your ExamEase password",
            $"<p>Hello {HtmlEncoder.Default.Encode(user.FullName)},</p><p>Click the link below to reset your password:</p>" +
            $"<p><a href=\"{HtmlEncoder.Default.Encode(link)}\">Reset password</a></p><p>If you did not request this, ignore this e-mail.</p>");
        await audit.LogAsync("ForgotPassword", "User", user.Id, userName: user.UserName);
        if (!delivered && env.IsDevelopment()) DevLink = link;
        return Page();
    }
}
