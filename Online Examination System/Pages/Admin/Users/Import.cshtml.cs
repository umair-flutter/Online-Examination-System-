using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Admin.Users;

public class ImportModel(ApplicationDbContext db, UserManager<ApplicationUser> users, ExcelService excel, AuditService audit) : PageModel
{
    [BindProperty] public IFormFile? Upload { get; set; }
    public ImportResult? Result { get; set; }

    public void OnGet() { }

    public IActionResult OnGetTemplate() => File(excel.UserTemplate(), ExcelService.ContentType, "users-template.xlsx");

    public async Task<IActionResult> OnPostAsync()
    {
        if (Upload is null || !Upload.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(Upload), "Please choose an .xlsx file.");
            return Page();
        }
        List<UserImportRow> rows;
        List<string> errors;
        try
        {
            await using var s = Upload.OpenReadStream();
            (rows, errors) = excel.ReadUsers(s);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(nameof(Upload), $"Could not read the file: {ex.Message}");
            return Page();
        }

        var classes = await db.ClassRooms.ToDictionaryAsync(c => c.Name, StringComparer.OrdinalIgnoreCase);
        var imported = 0;
        foreach (var r in rows)
        {
            var u = new ApplicationUser { UserName = r.UserName, Email = r.Email, FullName = r.FullName, StudentNumber = r.StudentNumber, EmailConfirmed = true };
            var res = await users.CreateAsync(u, r.Password);
            if (!res.Succeeded)
            {
                errors.Add($"Row {r.Row} ({r.UserName}): {string.Join(" ", res.Errors.Select(e => e.Description))}");
                continue;
            }
            await users.AddToRoleAsync(u, r.Role);
            if (r.Role == Roles.Student && r.ClassName != null)
            {
                if (!classes.TryGetValue(r.ClassName, out var c))
                {
                    c = new ClassRoom { Name = r.ClassName };
                    db.ClassRooms.Add(c);
                    classes[r.ClassName] = c;
                }
                c.Students.Add(new ClassStudent { StudentId = u.Id });
                await db.SaveChangesAsync();
            }
            imported++;
        }
        await audit.LogAsync("ImportUsers", "User", null, $"{imported} imported, {errors.Count} error(s)");
        Result = new ImportResult(imported, errors);
        return Page();
    }
}
