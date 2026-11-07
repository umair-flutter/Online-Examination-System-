using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using Online_Examination_System.Services;

namespace Online_Examination_System.Pages.Manage.Questions;

public class ImportModel(ApplicationDbContext db, ExcelService excel, AuditService audit) : PageModel
{
    [BindProperty] public IFormFile? Upload { get; set; }
    public ImportResult? Result { get; set; }
    public List<Subject> Subjects { get; set; } = [];

    public async Task OnGetAsync() => Subjects = await db.Subjects.OrderBy(s => s.Code).ToListAsync();

    public IActionResult OnGetTemplate() => File(excel.QuestionTemplate(), ExcelService.ContentType, "questions-template.xlsx");

    public async Task<IActionResult> OnPostAsync()
    {
        Subjects = await db.Subjects.OrderBy(s => s.Code).ToListAsync();
        if (Upload is null || !Upload.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(Upload), "Please choose an .xlsx file.");
            return Page();
        }
        List<QuestionImportRow> rows;
        List<string> errors;
        try
        {
            await using var s = Upload.OpenReadStream();
            (rows, errors) = excel.ReadQuestions(s);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(nameof(Upload), $"Could not read the file: {ex.Message}");
            return Page();
        }

        var subjects = Subjects.ToDictionary(s => s.Code, StringComparer.OrdinalIgnoreCase);
        var uid = User.UserId();
        var imported = 0;
        foreach (var r in rows)
        {
            if (!subjects.TryGetValue(r.SubjectCode, out var subject))
            {
                errors.Add($"Row {r.Row}: subject code '{r.SubjectCode}' does not exist.");
                continue;
            }
            db.Questions.Add(new Question
            {
                SubjectId = subject.Id, Text = r.Text, Type = r.Type, Marks = r.Marks, Difficulty = r.Difficulty, Explanation = r.Explanation,
                Tags = r.Tags, CreatedById = uid, CorrectShortAnswer = r.Type == QuestionType.ShortAnswer ? r.Correct : null,
                Choices = ExcelService.BuildChoices(r)
            });
            imported++;
        }
        await db.SaveChangesAsync();
        await audit.LogAsync("ImportQuestions", "Question", null, $"{imported} imported, {errors.Count} error(s)");
        Result = new ImportResult(imported, errors);
        return Page();
    }
}
