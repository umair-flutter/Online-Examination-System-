using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Student;

public class ResultsModel(ApplicationDbContext db, ExamService exams, ExcelService excel, PdfService pdf) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;
    public PagedResult<ExamAttempt> Items { get; set; } = null!;

    private IQueryable<ExamAttempt> Query()
    {
        var uid = User.UserId();
        return db.ExamAttempts.Include(a => a.Exam).ThenInclude(e => e.Subject)
            .Where(a => a.StudentId == uid && a.Status != AttemptStatus.InProgress)
            .OrderByDescending(a => a.SubmittedAt).AsNoTracking();
    }

    public async Task OnGetAsync()
    {
        await exams.FinalizeExpiredAttemptsAsync(User.UserId());
        Items = await Query().ToPagedAsync(PageNo, 15);
    }

    private static readonly string[] Headers = ["Exam", "Subject", "Attempt", "Submitted", "Score", "Total", "%", "Result"];
    private static IReadOnlyList<object?> Row(ExamAttempt a) =>
        [a.Exam.Title, a.Exam.Subject.Name, a.AttemptNumber, a.SubmittedAt, a.Score, a.TotalMarks, a.Percentage,
         a.Status == AttemptStatus.PendingMarking ? "Pending" : a.IsPassed ? "Pass" : "Fail"];

    public async Task<IActionResult> OnGetExcelAsync() =>
        File(excel.Export("My results", Headers, (await Query().ToListAsync()).Select(Row)), ExcelService.ContentType, "my-results.xlsx");

    public async Task<IActionResult> OnGetPdfAsync() =>
        File(pdf.Table("My results", User.Identity?.Name, Headers, (await Query().ToListAsync()).Select(Row)), PdfService.ContentType, "my-results.pdf");
}
