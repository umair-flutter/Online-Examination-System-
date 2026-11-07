using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Models;

namespace Online_Examination_System.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<ClassRoom> ClassRooms => Set<ClassRoom>();
    public DbSet<ClassStudent> ClassStudents => Set<ClassStudent>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionChoice> QuestionChoices => Set<QuestionChoice>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamClass> ExamClasses => Set<ExamClass>();
    public DbSet<ExamQuestion> ExamQuestions => Set<ExamQuestion>();
    public DbSet<ExamAttempt> ExamAttempts => Set<ExamAttempt>();
    public DbSet<AttemptAnswer> AttemptAnswers => Set<AttemptAnswer>();
    public DbSet<ProctorEvent> ProctorEvents => Set<ProctorEvent>();
    public DbSet<UserAttachment> UserAttachments => Set<UserAttachment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<ClassRoom>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Subject>().HasIndex(x => x.Code).IsUnique();

        b.Entity<ClassStudent>().HasKey(x => new { x.ClassRoomId, x.StudentId });
        b.Entity<ClassStudent>().HasOne(x => x.ClassRoom).WithMany(x => x.Students).HasForeignKey(x => x.ClassRoomId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ClassStudent>().HasOne(x => x.Student).WithMany(x => x.Classes).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Question>().HasOne(x => x.Subject).WithMany(x => x.Questions).HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Question>().HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        b.Entity<QuestionChoice>().HasOne(x => x.Question).WithMany(x => x.Choices).HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<Exam>().HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Exam>().HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Exam>().HasIndex(x => new { x.Status, x.StartDate, x.EndDate });

        b.Entity<ExamClass>().HasKey(x => new { x.ExamId, x.ClassRoomId });
        b.Entity<ExamClass>().HasOne(x => x.Exam).WithMany(x => x.Classes).HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ExamClass>().HasOne(x => x.ClassRoom).WithMany(x => x.Exams).HasForeignKey(x => x.ClassRoomId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<ExamQuestion>().HasOne(x => x.Exam).WithMany(x => x.Questions).HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ExamQuestion>().HasOne(x => x.Question).WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ExamQuestion>().HasIndex(x => new { x.ExamId, x.QuestionId }).IsUnique();

        b.Entity<ExamAttempt>().HasOne(x => x.Exam).WithMany(x => x.Attempts).HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ExamAttempt>().HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ExamAttempt>().HasIndex(x => new { x.ExamId, x.StudentId });
        b.Entity<ExamAttempt>().HasIndex(x => new { x.Status, x.Deadline });
        b.Entity<ExamAttempt>().HasIndex(x => x.CertificateCode);

        b.Entity<AttemptAnswer>().HasOne(x => x.Attempt).WithMany(x => x.Answers).HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<AttemptAnswer>().HasOne(x => x.Question).WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<AttemptAnswer>().HasIndex(x => new { x.AttemptId, x.QuestionId }).IsUnique();

        b.Entity<ProctorEvent>().HasOne(x => x.Attempt).WithMany(x => x.ProctorEvents).HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<UserAttachment>().HasOne(x => x.User).WithMany(x => x.Attachments).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Notification>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Notification>().HasIndex(x => new { x.UserId, x.IsRead });
        b.Entity<AuditLog>().HasIndex(x => x.CreatedAt);
    }
}
