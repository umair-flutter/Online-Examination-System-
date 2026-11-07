using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Admin.Subjects;

public class EditModel(ApplicationDbContext db, AuditService audit) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        public int Id { get; set; }
        [Required, StringLength(20)] public string Code { get; set; } = "";
        [Required, StringLength(150)] public string Name { get; set; } = "";
        [StringLength(500)] public string? Description { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null) return Page();
        var s = await db.Subjects.FindAsync(id);
        if (s is null) return NotFound();
        Input = new InputModel { Id = s.Id, Code = s.Code, Name = s.Name, Description = s.Description };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Input.Code = Input.Code.Trim().ToUpperInvariant();
        if (await db.Subjects.AnyAsync(s => s.Code == Input.Code && s.Id != Input.Id))
            ModelState.AddModelError("Input.Code", "This code is already used.");
        if (!ModelState.IsValid) return Page();

        Subject? s;
        if (Input.Id == 0) db.Subjects.Add(s = new Subject());
        else if ((s = await db.Subjects.FindAsync(Input.Id)) is null) return NotFound();
        s.Code = Input.Code;
        s.Name = Input.Name.Trim();
        s.Description = Input.Description;
        await db.SaveChangesAsync();
        await audit.LogAsync(Input.Id == 0 ? "Create" : "Update", "Subject", s.Id, s.Name);
        TempData[Toast.Success] = "Subject saved.";
        return RedirectToPage("Index");
    }
}
