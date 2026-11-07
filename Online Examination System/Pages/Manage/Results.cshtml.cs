using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Manage;

/// <summary>All submissions across the exams the user manages.</summary>
public class ResultsModel(ApplicationDbContext db, ExcelService excel, PdfService pdf) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int? ExamId { get; set; }
    [BindProperty(SupportsGet = true)] public int? ClassId { get; set; }
    /// <summary>pass | fail | pending</summary>
    [BindProperty(SupportsGet = true)] public string? Outcome { get; set; }
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;
    public PagedResult<ExamAttempt> Items { get; set; } = null!;
    public List<SelectListItem> ExamOptions { get; set; } = [];
    public List<SelectListItem> ClassOptions { get; set; } = [];

    private IQueryable<ExamAttempt> Query()
    {
        var exams = db.Exams.OwnedBy(User).Select(e => e.Id);
        var q = db.ExamAttempts.Include(a => a.Student).Include(a => a.Exam)
            .Where(a => a.Status != AttemptStatus.InProgress && exams.Contains(a.ExamId));
        if (!string.IsNullOrWhiteSpace(Search)) q = q.Where(a => a.Student.FullName.Contains(Search) || a.Student.UserName!.Contains(Search) || (a.Student.StudentNumber != null && a.Student.StudentNumber.Contains(Search)));
        if (ExamId is int e) q = q.Where(a => a.ExamId == e);
        if (ClassId is int c) q = q.Where(a => db.ClassStudents.Any(cs => cs.ClassRoomId == c && cs.StudentId == a.StudentId));
        q = Outcome switch
        {
            "pass" => q.Where(a => a.Status == AttemptStatus.Graded && a.IsPassed),
            "fail" => q.Where(a => a.Status == AttemptStatus.Graded && !a.IsPassed),
            "pending" => q.Where(a => a.Status == AttemptStatus.PendingMarking),
            _ => q
        };
        return q.OrderByDescending(a => a.SubmittedAt).AsNoTracking();
    }

    public async Task OnGetAsync()
    {
        ExamOptions = await db.Exams.OwnedBy(User).OrderBy(e => e.Title).Select(e => new SelectListItem(e.Title, e.Id.ToString())).ToListAsync();
        ClassOptions = await db.ClassRooms.OrderBy(c => c.Name).Select(c => new SelectListItem(c.Name, c.Id.ToString())).ToListAsync();
        Items = await Query().ToPagedAsync(PageNo, 20);
    }

    private static readonly string[] Headers = ["Student", "Username", "Exam", "Attempt", "Submitted", "Score", "Total", "%", "Result", "Violations"];
    private static IReadOnlyList<object?> Row(ExamAttempt a) =>
        [a.Student.FullName, a.Student.UserName, a.Exam.Title, a.AttemptNumber, a.SubmittedAt, a.Score, a.TotalMarks, a.Percentage,
         a.Status == AttemptStatus.PendingMarking ? "Pending" : a.IsPassed ? "Pass" : "Fail", a.ViolationCount];

    public async Task<IActionResult> OnGetExcelAsync() =>
        File(excel.Export("Results", Headers, (await Query().ToListAsync()).Select(Row)), ExcelService.ContentType, "results.xlsx");

    public async Task<IActionResult> OnGetPdfAsync() =>
        File(pdf.Table("Exam results", null, Headers, (await Query().Take(3000).ToListAsync()).Select(Row), landscape: true), PdfService.ContentType, "results.pdf");
}
