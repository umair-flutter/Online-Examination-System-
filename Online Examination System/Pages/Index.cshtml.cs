using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Models;

namespace Online_Examination_System.Pages;

public class IndexModel(ApplicationDbContext db) : PageModel
{
    public int ExamCount { get; set; }
    public int QuestionCount { get; set; }
    public int StudentCount { get; set; }

    public async Task OnGetAsync()
    {
        ExamCount = await db.Exams.CountAsync(e => e.Status != ExamStatus.Draft);
        QuestionCount = await db.Questions.CountAsync();
        var roleId = await db.Roles.Where(r => r.Name == Roles.Student).Select(r => r.Id).FirstOrDefaultAsync();
        StudentCount = await db.UserRoles.CountAsync(r => r.RoleId == roleId);
    }
}
