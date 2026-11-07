using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Models;

namespace Online_Examination_System.Pages;

/// <summary>Public certificate verification by certificate ID.</summary>
[AllowAnonymous]
public class VerifyModel(ApplicationDbContext db) : PageModel
{
    public string? Code { get; set; }
    public ExamAttempt? Attempt { get; set; }

    public async Task OnGetAsync(string? code)
    {
        Code = code?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(Code)) return;
        Attempt = await db.ExamAttempts.AsNoTracking()
            .Include(a => a.Student).Include(a => a.Exam).ThenInclude(e => e.Subject)
            .FirstOrDefaultAsync(a => a.CertificateCode == Code && a.IsPassed);
    }
}
