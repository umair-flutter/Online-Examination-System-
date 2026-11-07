using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Admin.Classes;

public class EditModel(ApplicationDbContext db, AuditService audit) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        public int Id { get; set; }
        [Required, StringLength(100)] public string Name { get; set; } = "";
        [StringLength(500)] public string? Description { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        if (id is null) return Page();
        var c = await db.ClassRooms.FindAsync(id);
        if (c is null) return NotFound();
        Input = new InputModel { Id = c.Id, Name = c.Name, Description = c.Description };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Input.Name = Input.Name?.Trim() ?? "";
        if (await db.ClassRooms.AnyAsync(c => c.Name == Input.Name && c.Id != Input.Id))
            ModelState.AddModelError("Input.Name", "A class with this name already exists.");
        if (!ModelState.IsValid) return Page();

        ClassRoom? c;
        if (Input.Id == 0) db.ClassRooms.Add(c = new ClassRoom());
        else if ((c = await db.ClassRooms.FindAsync(Input.Id)) is null) return NotFound();
        c.Name = Input.Name;
        c.Description = Input.Description;
        await db.SaveChangesAsync();
        await audit.LogAsync(Input.Id == 0 ? "Create" : "Update", "ClassRoom", c.Id, c.Name);
        TempData[Toast.Success] = "Class saved.";
        return Input.Id == 0 ? RedirectToPage("Students", new { id = c.Id }) : RedirectToPage("Index");
    }
}
