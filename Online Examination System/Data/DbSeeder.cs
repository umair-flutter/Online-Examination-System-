using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<ApplicationDbContext>();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();

        await db.Database.MigrateAsync();

        foreach (var r in Roles.All)
            if (!await roles.RoleExistsAsync(r)) await roles.CreateAsync(new IdentityRole(r));

        var admin = await EnsureUser(users, "nsadmin", "admin@examease.local", "System Administrator", "Admin@123", Roles.Admin);
        var teacher = await EnsureUser(users, "samantha", "samantha@examease.local", "Samantha Lee", "User@123", Roles.Teacher);
        var teacher2 = await EnsureUser(users, "marcus", "marcus@examease.local", "Marcus Brown", "User@123", Roles.Teacher);
        var athena = await EnsureUser(users, "athena", "athena@examease.local", "Athena Rivera", "User@123", Roles.Student, "S-1001");
        var students = new List<ApplicationUser> { athena };
        string[][] more =
        [
            ["brian", "Brian Carter", "S-1002"], ["chloe", "Chloe Nguyen", "S-1003"], ["daniel", "Daniel Kim", "S-1004"],
            ["emma", "Emma Wilson", "S-1005"], ["farhan", "Farhan Ali", "S-1006"], ["grace", "Grace Okafor", "S-1007"]
        ];
        foreach (var m in more)
            students.Add(await EnsureUser(users, m[0], $"{m[0]}@examease.local", m[1], "User@123", Roles.Student, m[2]));

        if (await db.Subjects.AnyAsync()) return; // demo content already seeded

        // ---------- classes & subjects ----------
        var classA = new ClassRoom { Name = "Class 10-A", Description = "Morning batch" };
        var classB = new ClassRoom { Name = "Class 10-B", Description = "Afternoon batch" };
        db.ClassRooms.AddRange(classA, classB);
        for (var i = 0; i < students.Count; i++)
            (i % 2 == 0 ? classA : classB).Students.Add(new ClassStudent { StudentId = students[i].Id });
        classB.Students.Add(new ClassStudent { StudentId = athena.Id }); // athena attends both

        var math = new Subject { Code = "MATH101", Name = "Mathematics", Description = "Basic algebra and arithmetic" };
        var sci = new Subject { Code = "SCI101", Name = "General Science" };
        var eng = new Subject { Code = "ENG101", Name = "English Language" };
        var cs = new Subject { Code = "CS101", Name = "Computer Science Fundamentals" };
        db.Subjects.AddRange(math, sci, eng, cs);
        await db.SaveChangesAsync();

        // ---------- question bank ----------
        Question Mcq(Subject s, string text, string[] choices, int correct, double marks = 1, Difficulty d = Difficulty.Easy, string? expl = null) => new()
        {
            Subject = s, Text = text, Type = QuestionType.MultipleChoice, Marks = marks, Difficulty = d, Explanation = expl, CreatedById = teacher.Id,
            Choices = choices.Select((c, i) => new QuestionChoice { Text = c, IsCorrect = i == correct, Order = i }).ToList()
        };
        Question Multi(Subject s, string text, string[] choices, int[] correct, double marks = 2) => new()
        {
            Subject = s, Text = text, Type = QuestionType.MultipleAnswer, Marks = marks, Difficulty = Difficulty.Medium, CreatedById = teacher.Id,
            Choices = choices.Select((c, i) => new QuestionChoice { Text = c, IsCorrect = correct.Contains(i), Order = i }).ToList()
        };
        Question Tf(Subject s, string text, bool answer, string? expl = null) => new()
        {
            Subject = s, Text = text, Type = QuestionType.TrueFalse, Marks = 1, Difficulty = Difficulty.Easy, Explanation = expl, CreatedById = teacher.Id,
            Choices = [new() { Text = "True", IsCorrect = answer, Order = 0 }, new() { Text = "False", IsCorrect = !answer, Order = 1 }]
        };
        Question Short(Subject s, string text, string accepted, double marks = 1) => new()
        {
            Subject = s, Text = text, Type = QuestionType.ShortAnswer, Marks = marks, Difficulty = Difficulty.Medium, CorrectShortAnswer = accepted, CreatedById = teacher.Id
        };
        Question Essay(Subject s, string text, double marks = 5) => new()
        {
            Subject = s, Text = text, Type = QuestionType.Essay, Marks = marks, Difficulty = Difficulty.Hard, CreatedById = teacher.Id
        };

        var mathQs = new List<Question>
        {
            Mcq(math, "What is 12 × 8?", ["86", "96", "108", "112"], 1, expl: "12 × 8 = 96"),
            Mcq(math, "Solve for x: 2x + 6 = 14", ["3", "4", "5", "8"], 1, 2, Difficulty.Medium, "2x = 8, so x = 4"),
            Tf(math, "The square root of 81 is 9.", true),
            Multi(math, "Which of the following are prime numbers?", ["2", "9", "11", "15", "17"], [0, 2, 4]),
            Short(math, "What is 15% of 200? (number only)", "30"),
            Mcq(math, "What is the value of π rounded to two decimals?", ["3.12", "3.14", "3.16", "3.41"], 1),
        };
        var sciQs = new List<Question>
        {
            Mcq(sci, "Which planet is known as the Red Planet?", ["Venus", "Mars", "Jupiter", "Saturn"], 1),
            Tf(sci, "Sound travels faster than light.", false, "Light is about 880,000 times faster."),
            Mcq(sci, "What gas do plants absorb from the atmosphere?", ["Oxygen", "Nitrogen", "Carbon dioxide", "Helium"], 2),
            Short(sci, "What is the chemical symbol for water?", "H2O|H₂O"),
            Multi(sci, "Which of these are mammals?", ["Whale", "Shark", "Bat", "Penguin"], [0, 2]),
        };
        var engQs = new List<Question>
        {
            Mcq(eng, "Choose the correct spelling:", ["Recieve", "Receive", "Receeve", "Riceive"], 1),
            Short(eng, "What is the past tense of 'run'?", "ran"),
            Tf(eng, "'Their' and 'there' have the same meaning.", false),
            Essay(eng, "In about 150 words, describe a person who inspires you and explain why."),
        };
        var csQs = new List<Question>
        {
            Mcq(cs, "What does CPU stand for?", ["Central Processing Unit", "Computer Personal Unit", "Central Program Utility", "Core Processing Unit"], 0),
            Tf(cs, "1 byte equals 8 bits.", true),
            Mcq(cs, "Which of these is NOT a programming language?", ["Python", "C#", "HTML", "Java"], 2, expl: "HTML is a markup language."),
            Multi(cs, "Which are valid HTTP methods?", ["GET", "FETCH", "POST", "DELETE"], [0, 2, 3]),
            Short(cs, "What is the binary representation of decimal 5?", "101"),
            Essay(cs, "Explain the difference between RAM and ROM.", 4),
        };
        db.Questions.AddRange(mathQs.Concat(sciQs).Concat(engQs).Concat(csQs));
        await db.SaveChangesAsync();

        // ---------- exams ----------
        var now = DateTime.Now;
        Exam MakeExam(string title, Subject s, List<Question> qs, DateTime start, DateTime end, int minutes, Action<Exam>? configure = null, params ClassRoom[] classes)
        {
            var e = new Exam
            {
                Title = title, Subject = s, StartDate = start, EndDate = end, DurationMinutes = minutes, Status = ExamStatus.Published,
                CreatedById = teacher.Id, Description = $"{s.Name} assessment", PassPercentage = 50,
                Instructions = "Answer every question. Save each answer before moving to the next question. The exam submits automatically when the timer reaches zero.",
                Questions = qs.Select((q, i) => new ExamQuestion { Question = q, Order = i }).ToList(),
                Classes = classes.Select(c => new ExamClass { ClassRoom = c }).ToList()
            };
            configure?.Invoke(e);
            db.Exams.Add(e);
            return e;
        }

        MakeExam("Mathematics Mid-Term", math, mathQs, now.AddDays(-1), now.AddDays(7), 20, e => e.RandomizeQuestions = true, classA, classB);
        MakeExam("Science Quiz (strict)", sci, sciQs, now.AddHours(-2), now.AddDays(3), 10, e =>
        {
            e.ShowAnswersToStudents = false; e.RandomizeChoices = true; e.AllowBackNavigation = false;
            e.EnableProctoring = true; e.MaxViolations = 5; e.NegativeMarking = 0.25;
        }, classA);
        MakeExam("English Final Exam", eng, engQs, now.AddDays(3), now.AddDays(5), 45, null, classA, classB);
        var csExam = MakeExam("Computer Science Basics", cs, csQs, now.AddDays(-10), now.AddDays(-1), 30, e => e.MaxAttempts = 2);
        db.Exams.Add(new Exam
        {
            Title = "Algebra Practice (draft)", Subject = math, StartDate = now.AddDays(10), EndDate = now.AddDays(12),
            DurationMinutes = 15, CreatedById = teacher2.Id, Status = ExamStatus.Draft,
            Questions = mathQs.Take(3).Select((q, i) => new ExamQuestion { Question = q, Order = i }).ToList()
        });
        await db.SaveChangesAsync();

        // ---------- historical attempts for the closed exam (feeds the dashboard charts) ----------
        var rnd = new Random(42);
        var qDict = csQs.ToDictionary(q => q.Id);
        var dayOffset = 9;
        foreach (var st in students)
        {
            var started = now.AddDays(-dayOffset--).AddHours(rnd.Next(1, 6));
            var attempt = new ExamAttempt
            {
                Exam = csExam, StudentId = st.Id, StartedAt = started, Deadline = started.AddMinutes(30),
                SubmittedAt = started.AddMinutes(rnd.Next(8, 28)), QuestionOrder = string.Join(',', csQs.Select(q => q.Id)), IpAddress = "127.0.0.1"
            };
            var skill = rnd.NextDouble();
            foreach (var q in csQs)
            {
                var a = new AttemptAnswer { QuestionId = q.Id, IsAnswered = true, AnsweredAt = attempt.SubmittedAt, ChoiceOrder = string.Join(',', q.Choices.Select(c => c.Id)) };
                var right = rnd.NextDouble() < 0.35 + skill * 0.6;
                switch (q.Type)
                {
                    case QuestionType.ShortAnswer: a.TextAnswer = right ? q.CorrectShortAnswer : "110"; break;
                    case QuestionType.Essay:
                        a.TextAnswer = "RAM is volatile memory used while programs run; ROM is non-volatile and stores firmware.";
                        break;
                    default:
                        var ids = right ? q.Choices.Where(c => c.IsCorrect) : q.Choices.Where(c => !c.IsCorrect).Take(1);
                        a.SelectedChoiceIds = string.Join(',', ids.Select(c => c.Id));
                        break;
                }
                GradingService.GradeAnswer(a, q, csExam);
                if (q.Type == QuestionType.Essay && st != students[^1]) // leave the last one pending to demo the marking page
                {
                    a.MarksAwarded = Math.Round(q.Marks * (0.4 + skill * 0.6)); a.ManuallyMarked = true; a.NeedsManualMarking = true;
                    a.MarkedById = teacher.Id; a.MarkedAt = attempt.SubmittedAt?.AddDays(1); a.Feedback = "Good explanation.";
                }
                attempt.Answers.Add(a);
            }
            GradingService.RecalculateTotals(attempt, csExam, qDict);
            db.ExamAttempts.Add(attempt);
        }

        db.Notifications.Add(new Notification { UserId = athena.Id, Title = "Welcome to ExamEase", Message = "Check the Exams page to see the exams available to you.", Link = "/Student/Exams" });
        db.AuditLogs.Add(new AuditLog { Action = "Seed", Details = "Demo data created", UserName = "system" });
        await db.SaveChangesAsync();
    }

    private static async Task<ApplicationUser> EnsureUser(UserManager<ApplicationUser> users, string userName, string email, string name, string password, string role, string? studentNo = null)
    {
        var u = await users.FindByNameAsync(userName);
        if (u != null) return u;
        u = new ApplicationUser { UserName = userName, Email = email, EmailConfirmed = true, FullName = name, StudentNumber = studentNo };
        var res = await users.CreateAsync(u, password);
        if (!res.Succeeded) throw new InvalidOperationException(string.Join("; ", res.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(u, role);
        return u;
    }
}
