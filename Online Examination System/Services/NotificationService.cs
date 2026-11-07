using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Models;

namespace Online_Examination_System.Services;

public class NotificationService(ApplicationDbContext db)
{
    public async Task SendAsync(string userId, string title, string message, string? link = null)
    {
        db.Notifications.Add(new Notification { UserId = userId, Title = title, Message = message, Link = link });
        await db.SaveChangesAsync();
    }

    public async Task SendManyAsync(IEnumerable<string> userIds, string title, string message, string? link = null)
    {
        foreach (var id in userIds.Distinct())
            db.Notifications.Add(new Notification { UserId = id, Title = title, Message = message, Link = link });
        await db.SaveChangesAsync();
    }

    /// <summary>Notifies the students who can take an exam (students of the assigned classes, or all students when no class is assigned).</summary>
    public async Task NotifyExamPublishedAsync(Exam exam)
    {
        var classIds = await db.ExamClasses.Where(x => x.ExamId == exam.Id).Select(x => x.ClassRoomId).ToListAsync();
        List<string> students;
        if (classIds.Count > 0)
            students = await db.ClassStudents.Where(x => classIds.Contains(x.ClassRoomId)).Select(x => x.StudentId).Distinct().ToListAsync();
        else
        {
            var roleId = await db.Roles.Where(r => r.Name == Roles.Student).Select(r => r.Id).FirstAsync();
            students = await db.UserRoles.Where(r => r.RoleId == roleId).Select(r => r.UserId).ToListAsync();
        }
        await SendManyAsync(students, "New exam available",
            $"\"{exam.Title}\" is open from {exam.StartDate:g} to {exam.EndDate:g}.", "/Student/Exams");
    }

    public async Task NotifyStaffOfPendingMarkingAsync(Exam exam)
    {
        var exists = await db.Notifications.AnyAsync(n => n.UserId == exam.CreatedById && !n.IsRead && n.Link == $"/Manage/Marking?examId={exam.Id}");
        if (!exists)
            await SendAsync(exam.CreatedById, "Answers waiting for marking",
                $"New submissions for \"{exam.Title}\" contain essay answers that need marking.", $"/Manage/Marking?examId={exam.Id}");
    }

    public Task<int> UnreadCountAsync(string userId) => db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
}
