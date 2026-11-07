using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace Online_Examination_System.Models;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Teacher = "Teacher";
    public const string Student = "Student";
    public const string Staff = Admin + "," + Teacher;
    public static readonly string[] All = [Admin, Teacher, Student];
}

public enum UserStatus { Active = 0, Inactive = 1, Suspended = 2 }

public class ApplicationUser : IdentityUser
{
    [MaxLength(150)] public string FullName { get; set; } = "";
    [MaxLength(300)] public string? ProfilePicture { get; set; }
    [MaxLength(30)] public string? StudentNumber { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Active;
    [MaxLength(10)] public string? PreferredLanguage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? LastLoginAt { get; set; }

    public ICollection<ClassStudent> Classes { get; set; } = new List<ClassStudent>();
    public ICollection<UserAttachment> Attachments { get; set; } = new List<UserAttachment>();
}

public class ClassRoom
{
    public int Id { get; set; }
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(500)] public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public ICollection<ClassStudent> Students { get; set; } = new List<ClassStudent>();
    public ICollection<ExamClass> Exams { get; set; } = new List<ExamClass>();
}

public class ClassStudent
{
    public int ClassRoomId { get; set; }
    public ClassRoom ClassRoom { get; set; } = null!;
    public string StudentId { get; set; } = "";
    public ApplicationUser Student { get; set; } = null!;
    public DateTime JoinedAt { get; set; } = DateTime.Now;
}

public class Subject
{
    public int Id { get; set; }
    [Required, MaxLength(20)] public string Code { get; set; } = "";
    [Required, MaxLength(150)] public string Name { get; set; } = "";
    [MaxLength(500)] public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public ICollection<Question> Questions { get; set; } = new List<Question>();
}

public enum QuestionType
{
    [Display(Name = "Multiple Choice")] MultipleChoice = 0,
    [Display(Name = "True / False")] TrueFalse = 1,
    [Display(Name = "Short Answer")] ShortAnswer = 2,
    [Display(Name = "Essay")] Essay = 3,
    [Display(Name = "Multiple Answer")] MultipleAnswer = 4
}

public enum Difficulty { Easy = 0, Medium = 1, Hard = 2 }

public class Question
{
    public int Id { get; set; }
    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;
    [Required] public string Text { get; set; } = "";
    public QuestionType Type { get; set; }
    public double Marks { get; set; } = 1;
    public Difficulty Difficulty { get; set; } = Difficulty.Medium;
    [MaxLength(300)] public string? ImagePath { get; set; }
    [MaxLength(2000)] public string? Explanation { get; set; }
    /// <summary>Accepted answers for short answer questions, separated by "|".</summary>
    [MaxLength(1000)] public string? CorrectShortAnswer { get; set; }
    public bool CaseSensitive { get; set; }
    [MaxLength(200)] public string? Tags { get; set; }
    public string CreatedById { get; set; } = "";
    public ApplicationUser CreatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
    public ICollection<QuestionChoice> Choices { get; set; } = new List<QuestionChoice>();

    public bool HasChoices => Type is QuestionType.MultipleChoice or QuestionType.TrueFalse or QuestionType.MultipleAnswer;
    public bool IsAutoGraded => Type != QuestionType.Essay;
}

public class QuestionChoice
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    [Required, MaxLength(1000)] public string Text { get; set; } = "";
    public bool IsCorrect { get; set; }
    public int Order { get; set; }
}

public enum ExamStatus { Draft = 0, Published = 1, Archived = 2 }

public class Exam
{
    public int Id { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    [MaxLength(2000)] public string? Description { get; set; }
    [MaxLength(4000)] public string? Instructions { get; set; }
    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    /// <summary>Time limit in minutes.</summary>
    public int DurationMinutes { get; set; } = 30;
    public double PassPercentage { get; set; } = 50;
    public bool ShowAnswersToStudents { get; set; } = true;
    public bool RandomizeQuestions { get; set; }
    public bool RandomizeChoices { get; set; }
    public bool AllowBackNavigation { get; set; } = true;
    public int MaxAttempts { get; set; } = 1;
    /// <summary>Fraction of the question marks deducted for a wrong objective answer (0 = disabled, 0.25 = quarter).</summary>
    public double NegativeMarking { get; set; }
    public bool PartialCreditMultipleAnswer { get; set; }
    [MaxLength(50)] public string? AccessCode { get; set; }
    public bool EnableProctoring { get; set; }
    /// <summary>Auto-submit after this many proctoring violations (0 = only record).</summary>
    public int MaxViolations { get; set; }
    public ExamStatus Status { get; set; } = ExamStatus.Draft;
    public string CreatedById { get; set; } = "";
    public ApplicationUser CreatedBy { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public ICollection<ExamQuestion> Questions { get; set; } = new List<ExamQuestion>();
    public ICollection<ExamClass> Classes { get; set; } = new List<ExamClass>();
    public ICollection<ExamAttempt> Attempts { get; set; } = new List<ExamAttempt>();
}

public class ExamClass
{
    public int ExamId { get; set; }
    public Exam Exam { get; set; } = null!;
    public int ClassRoomId { get; set; }
    public ClassRoom ClassRoom { get; set; } = null!;
}

public class ExamQuestion
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public Exam Exam { get; set; } = null!;
    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    public int Order { get; set; }
}

public enum AttemptStatus
{
    InProgress = 0,
    [Display(Name = "Pending Marking")] PendingMarking = 1,
    Graded = 2
}

public class ExamAttempt
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public Exam Exam { get; set; } = null!;
    public string StudentId { get; set; } = "";
    public ApplicationUser Student { get; set; } = null!;
    public int AttemptNumber { get; set; } = 1;
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime Deadline { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public AttemptStatus Status { get; set; } = AttemptStatus.InProgress;
    public double Score { get; set; }
    public double TotalMarks { get; set; }
    public double Percentage { get; set; }
    public bool IsPassed { get; set; }
    /// <summary>Comma separated question ids in the order shown to this student.</summary>
    public string QuestionOrder { get; set; } = "";
    public int ViolationCount { get; set; }
    public bool AutoSubmitted { get; set; }
    [MaxLength(64)] public string? IpAddress { get; set; }
    [MaxLength(64)] public string? CertificateCode { get; set; }

    public ICollection<AttemptAnswer> Answers { get; set; } = new List<AttemptAnswer>();
    public ICollection<ProctorEvent> ProctorEvents { get; set; } = new List<ProctorEvent>();

    public List<int> QuestionIds => QuestionOrder.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
    public bool IsSubmitted => Status != AttemptStatus.InProgress;
}

public class AttemptAnswer
{
    public int Id { get; set; }
    public int AttemptId { get; set; }
    public ExamAttempt Attempt { get; set; } = null!;
    public int QuestionId { get; set; }
    public Question Question { get; set; } = null!;
    /// <summary>Comma separated choice ids selected by the student.</summary>
    [MaxLength(500)] public string? SelectedChoiceIds { get; set; }
    /// <summary>Comma separated choice ids in the order displayed to the student.</summary>
    [MaxLength(500)] public string? ChoiceOrder { get; set; }
    public string? TextAnswer { get; set; }
    public bool IsAnswered { get; set; }
    public bool? IsCorrect { get; set; }
    public double? MarksAwarded { get; set; }
    public bool NeedsManualMarking { get; set; }
    public bool ManuallyMarked { get; set; }
    [MaxLength(2000)] public string? Feedback { get; set; }
    public string? MarkedById { get; set; }
    public DateTime? MarkedAt { get; set; }
    public DateTime? AnsweredAt { get; set; }

    public HashSet<int> SelectedIds => (SelectedChoiceIds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToHashSet();
    public List<int> ChoiceOrderIds => (ChoiceOrder ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
}

public class ProctorEvent
{
    public long Id { get; set; }
    public int AttemptId { get; set; }
    public ExamAttempt Attempt { get; set; } = null!;
    [MaxLength(50)] public string EventType { get; set; } = "";
    [MaxLength(500)] public string? Details { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.Now;
}

public class UserAttachment
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    [MaxLength(260)] public string FileName { get; set; } = "";
    [MaxLength(300)] public string StoredName { get; set; } = "";
    [MaxLength(150)] public string ContentType { get; set; } = "";
    public long Size { get; set; }
    [MaxLength(300)] public string? Description { get; set; }
    [MaxLength(256)] public string? UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.Now;
}

public class AuditLog
{
    public long Id { get; set; }
    [MaxLength(450)] public string? UserId { get; set; }
    [MaxLength(256)] public string? UserName { get; set; }
    [MaxLength(100)] public string Action { get; set; } = "";
    [MaxLength(100)] public string? Entity { get; set; }
    [MaxLength(100)] public string? EntityId { get; set; }
    [MaxLength(2000)] public string? Details { get; set; }
    [MaxLength(64)] public string? IpAddress { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public class Notification
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    [MaxLength(200)] public string Title { get; set; } = "";
    [MaxLength(1000)] public string Message { get; set; } = "";
    [MaxLength(300)] public string? Link { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>View model used by the _AnswerReview partial.</summary>
public record AttemptReview(ExamAttempt Attempt, IReadOnlyDictionary<int, Question> Questions, bool ShowCorrect);
