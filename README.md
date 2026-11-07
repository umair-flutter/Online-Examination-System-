# ExamEase – Online Examination System (ASP.NET Core 10)

A full online examination system built with **ASP.NET Core Razor Pages (.NET 10)**, **EF Core + SQL Server**, **ASP.NET Core Identity**, **Bootstrap 5.3**, **Chart.js**, **ClosedXML** (Excel) and **QuestPDF** (PDF).

## Getting started

1. Requirements: .NET SDK 10, SQL Server LocalDB (installed with Visual Studio) or any SQL Server.
2. Check the connection string in `Online Examination System/appsettings.json` (`DefaultConnection`).
3. Run:
   ```bash
   dotnet run --project "Online Examination System/Online Examination System.csproj"
   ```
   The database is created and migrated automatically on first start, and demo data is seeded.
4. Open http://localhost:5019

### Demo accounts

| Role    | Username   | Password    |
|---------|------------|-------------|
| Admin   | `nsadmin`  | `Admin@123` |
| Teacher | `samantha` | `User@123`  |
| Teacher | `marcus`   | `User@123`  |
| Student | `athena`   | `User@123`  |
| Student | `brian`, `chloe`, `daniel`, `emma`, `farhan`, `grace` | `User@123` |

### E-mail (forgot password)

Fill in the `Smtp` section of `appsettings.json`. When `Smtp:Host` is empty, e-mails are written to `App_Data/emails/*.html`; in Development the reset link is also shown on screen.

## Features

**Same as ExamEase**
- Dashboard charts (submissions per day, pass/fail, average per exam, top 5 wrongly answered questions, question types)
- Class, subject and user management; user status (Active / Inactive / Suspended – blocked users cannot log in and are signed out within a minute); user attachments
- Question bank: Multiple choice, True/False, Short answer, Essay, Multiple answer; import/export with Excel
- Exams: schedule start/end date, time limit with countdown, show/hide answers, randomize questions, clone exam
- Strict mode: students must save the current answer before moving on; skipping via URL redirects back to the current question
- Regrading: changing a question's correct answer (or marks) recalculates every affected result automatically
- Manual marking page for essays
- Admin sees everything; teachers only their own questions and exams
- Register, login, forgot/reset password, profile with picture, change password
- Export to Excel and PDF on every list
- Multilanguage (English, Arabic, Urdu, French) with RTL layout for Arabic and Urdu
- Responsive layout, Bootstrap 5, public home page

**Added on top**
- **Server-enforced timer** – the deadline is stored on the server; a background worker auto-submits expired attempts even if the browser was closed; the exam resumes where the student left off
- **Proctoring** – full-screen requirement, tab-switch / focus-loss / full-screen-exit detection, copy/paste/right-click blocking, per-attempt event log, optional auto-submit after N violations
- **Access code** per exam, **max attempts** (retakes), optional **no back navigation**
- **Shuffle answer choices** per student
- **Negative marking** and **partial credit** for multiple-answer questions
- **Pass mark**, pass/fail, and **PDF certificates** with a public **verification page** (`/Verify`)
- Correct answers revealed only when the exam is closed or the student has no attempts left (prevents answer sharing)
- **Exam analytics**: participation, average/high/low, pass rate, average time, score distribution, **item analysis** (correct rate per question), leaderboard
- **Override marks** of any auto-graded answer with feedback to the student
- **Random question picker** (add N random questions matching filters), question reordering, images and tags on questions, difficulty levels
- **In-app notifications** (new exam published, result graded / re-graded, essays waiting for marking)
- **Audit log** of logins, failed logins, lockouts and every create/update/delete, with Excel/PDF export
- Account lockout after 5 failed logins, **dark mode**, per-user preferred language
- Bulk **user import from Excel** (creates classes automatically)
- Result slip PDF for students and staff, reset an attempt to allow a retake

## Project structure

```
Online Examination System/
├─ Data/             DbContext, migrations, demo-data seeder
├─ Models/           Entities (users, classes, subjects, questions, exams, attempts, answers, logs…)
├─ Services/         GradingService (grading + regrading), ExamService, Excel/PDF, e-mail, files, audit, notifications, Lang (i18n)
├─ Infrastructure/   Paging and helper extensions
├─ Pages/
│  ├─ Account/       Login, Register, Forgot/Reset password, Profile, Change password
│  ├─ Admin/         Users, Classes, Subjects, Audit logs           (Admin only)
│  ├─ Manage/        Questions, Exams, Marking, Results             (Admin + Teacher)
│  ├─ Student/       Exams, Start, Take (exam player), Result(s)    (Student only)
│  └─ Dashboard, Notifications, Verify, Index
└─ Resources/Lang/   ar.json, ur.json, fr.json – English text is the key; add a file + entry in Lang.Supported to add a language
```

## Notes

- All dates are stored in server local time (single-time-zone deployment).
- Uploaded profile pictures and question images go to `wwwroot/uploads`; user attachments are stored privately in `App_Data/attachments` and served only through authorized handlers.
- QuestPDF is used under its Community license; check whether your organisation qualifies.
