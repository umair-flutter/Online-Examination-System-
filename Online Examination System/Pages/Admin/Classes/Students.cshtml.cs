using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Admin.Classes;

public record StudentPick(string Id, string FullName, string UserName, string? StudentNumber);

public class StudentsModel(ApplicationDbContext db, AuditService audit) : PageModel
{
    public ClassRoom ClassRoom { get; set; } = null!;
    public List<StudentPick> Members { get; set; } = [];
    public List<StudentPick> Available { get; set; } = [];
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }

    private async Task<bool> LoadAsync(int id)
    {
        var c = await db.ClassRooms.FindAsync(id);
        if (c is null) return false;
        ClassRoom = c;
        var roleId = await db.Roles.Where(r => r.Name == Roles.Student).Select(r => r.Id).FirstAsync();
        var studentIds = db.UserRoles.Where(r => r.RoleId == roleId).Select(r => r.UserId);
        var memberIds = db.ClassStudents.Where(x => x.ClassRoomId == id).Select(x => x.StudentId);

        Members = await db.Users.Where(u => memberIds.Contains(u.Id)).OrderBy(u => u.FullName)
            .Select(u => new StudentPick(u.Id, u.FullName, u.UserName!, u.StudentNumber)).ToListAsync();
        var avail = db.Users.Where(u => studentIds.Contains(u.Id) && !memberIds.Contains(u.Id));
        if (!string.IsNullOrWhiteSpace(Search))
            avail = avail.Where(u => u.FullName.Contains(Search) || u.UserName!.Contains(Search) || (u.StudentNumber != null && u.StudentNumber.Contains(Search)));
        Available = await avail.OrderBy(u => u.FullName).Take(200)
            .Select(u => new StudentPick(u.Id, u.FullName, u.UserName!, u.StudentNumber)).ToListAsync();
        return true;
    }

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAddAsync(int id, List<string> studentIds)
    {
        if (!await db.ClassRooms.AnyAsync(c => c.Id == id)) return NotFound();
        var existing = await db.ClassStudents.Where(x => x.ClassRoomId == id).Select(x => x.StudentId).ToListAsync();
        var toAdd = studentIds.Except(existing).Distinct().ToList();
        db.ClassStudents.AddRange(toAdd.Select(s => new ClassStudent { ClassRoomId = id, StudentId = s }));
        await db.SaveChangesAsync();
        await audit.LogAsync("AddStudents", "ClassRoom", id, $"{toAdd.Count} student(s)");
        TempData[Toast.Success] = $"{toAdd.Count} student(s) added.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostRemoveAsync(int id, string studentId)
    {
        var cs = await db.ClassStudents.FindAsync(id, studentId);
        if (cs != null)
        {
            db.ClassStudents.Remove(cs);
            await db.SaveChangesAsync();
            await audit.LogAsync("RemoveStudent", "ClassRoom", id, studentId);
        }
        TempData[Toast.Success] = "Student removed from class.";
        return RedirectToPage(new { id });
    }
}
