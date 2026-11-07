using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Online_Examination_System.Services;

public class PdfService
{
    public const string ContentType = "application/pdf";
    private const string Brand = "#4f46e5";

    /// <summary>Generic landscape/portrait table report.</summary>
    public byte[] Table(string title, string? subtitle, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows, bool landscape = false)
    {
        var data = rows.ToList();
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
            page.Margin(30);
            page.DefaultTextStyle(t => t.FontSize(9));
            Header(page, title, subtitle);
            page.Content().PaddingTop(10).Table(table =>
            {
                table.ColumnsDefinition(c => { foreach (var _ in headers) c.RelativeColumn(); });
                table.Header(h =>
                {
                    foreach (var head in headers)
                        h.Cell().Background(Brand).Padding(4).Text(head).FontColor(Colors.White).SemiBold();
                });
                var i = 0;
                foreach (var row in data)
                {
                    var bg = i++ % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                    foreach (var cell in row)
                        table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(Format(cell));
                }
            });
            Footer(page);
        })).GeneratePdf();
    }

    private static string Format(object? v) => v switch
    {
        null => "",
        DateTime d => d.ToString("yyyy-MM-dd HH:mm"),
        bool b => b ? "Yes" : "No",
        double d => d.ToString("0.##"),
        Enum e => e.DisplayName(),
        _ => v.ToString() ?? ""
    };

    private static void Header(PageDescriptor page, string title, string? subtitle) =>
        page.Header().BorderBottom(2).BorderColor(Brand).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("ExamEase").FontSize(10).FontColor(Brand).SemiBold();
                col.Item().Text(title).FontSize(16).Bold();
                if (!string.IsNullOrEmpty(subtitle)) col.Item().Text(subtitle).FontColor(Colors.Grey.Darken1);
            });
            row.ConstantItem(140).AlignRight().AlignBottom().Text($"Generated {DateTime.Now:yyyy-MM-dd HH:mm}").FontSize(8).FontColor(Colors.Grey.Darken1);
        });

    private static void Footer(PageDescriptor page) =>
        page.Footer().AlignCenter().Text(t =>
        {
            t.DefaultTextStyle(s => s.FontSize(8).FontColor(Colors.Grey.Darken1));
            t.Span("Page "); t.CurrentPageNumber(); t.Span(" of "); t.TotalPages();
        });

    /// <summary>Result slip for a single attempt. Answers are included only when <paramref name="includeAnswers"/> is true.</summary>
    public byte[] ResultSlip(ExamAttempt a, IReadOnlyDictionary<int, Question> questions, bool includeAnswers)
    {
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(35);
            page.DefaultTextStyle(t => t.FontSize(10));
            Header(page, "Exam Result", a.Exam.Title);
            page.Content().PaddingTop(12).Column(col =>
            {
                col.Spacing(6);
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { c.ConstantColumn(130); c.RelativeColumn(); });
                    void R(string k, string v) { t.Cell().Padding(3).Text(k).SemiBold(); t.Cell().Padding(3).Text(v); }
                    R("Student", $"{a.Student.FullName} ({a.Student.UserName})");
                    R("Subject", a.Exam.Subject?.Name ?? "");
                    R("Attempt", a.AttemptNumber.ToString());
                    R("Started", a.StartedAt.ToString("yyyy-MM-dd HH:mm"));
                    R("Submitted", a.SubmittedAt?.ToString("yyyy-MM-dd HH:mm") ?? "-");
                    R("Score", $"{a.Score:0.##} / {a.TotalMarks:0.##} ({a.Percentage:0.##}%)");
                    R("Status", a.Status == AttemptStatus.PendingMarking ? "Pending marking" : a.IsPassed ? "PASSED" : "FAILED");
                });

                if (!includeAnswers) return;
                col.Item().PaddingTop(10).Text("Answers").FontSize(13).Bold();
                var n = 1;
                foreach (var qid in a.QuestionIds)
                {
                    if (!questions.TryGetValue(qid, out var q)) continue;
                    var ans = a.Answers.FirstOrDefault(x => x.QuestionId == qid);
                    col.Item().Border(0.5f).BorderColor(Colors.Grey.Lighten1).Padding(6).Column(qc =>
                    {
                        qc.Item().Text($"{n++}. {q.Text}").SemiBold();
                        string given = ans is null || !ans.IsAnswered ? "(not answered)"
                            : q.HasChoices ? string.Join(", ", q.Choices.Where(c => ans.SelectedIds.Contains(c.Id)).Select(c => c.Text))
                            : ans.TextAnswer ?? "";
                        qc.Item().Text($"Your answer: {given}");
                        if (q.HasChoices) qc.Item().Text($"Correct answer: {string.Join(", ", q.Choices.Where(c => c.IsCorrect).Select(c => c.Text))}").FontColor(Colors.Green.Darken2);
                        else if (q.Type == QuestionType.ShortAnswer) qc.Item().Text($"Accepted: {q.CorrectShortAnswer?.Replace("|", " / ")}").FontColor(Colors.Green.Darken2);
                        qc.Item().Text($"Marks: {(ans?.MarksAwarded?.ToString("0.##") ?? "pending")} / {q.Marks:0.##}").FontColor(Colors.Grey.Darken2);
                        if (!string.IsNullOrEmpty(ans?.Feedback)) qc.Item().Text($"Feedback: {ans.Feedback}").Italic();
                    });
                }
            });
            Footer(page);
        })).GeneratePdf();
    }

    public byte[] Certificate(ExamAttempt a, string verifyUrl)
    {
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(25);
            page.Content().Border(6).BorderColor(Brand).Padding(8).Border(1).BorderColor(Colors.Grey.Lighten1).Padding(40)
                .AlignCenter().AlignMiddle().Column(col =>
                {
                    col.Spacing(14);
                    col.Item().AlignCenter().Text("ExamEase").FontSize(14).FontColor(Brand).SemiBold().LetterSpacing(0.2f);
                    col.Item().AlignCenter().Text("CERTIFICATE OF ACHIEVEMENT").FontSize(30).Bold();
                    col.Item().AlignCenter().Text("This certifies that").FontSize(13).FontColor(Colors.Grey.Darken2);
                    col.Item().AlignCenter().Text(a.Student.FullName).FontSize(34).Bold().FontColor(Brand);
                    col.Item().AlignCenter().Text("has successfully passed the examination").FontSize(13).FontColor(Colors.Grey.Darken2);
                    col.Item().AlignCenter().Text(a.Exam.Title).FontSize(20).SemiBold();
                    col.Item().AlignCenter().Text($"with a score of {a.Percentage:0.##}% on {a.SubmittedAt:MMMM d, yyyy}").FontSize(13);
                    col.Item().PaddingTop(25).AlignCenter().Text($"Certificate ID: {a.CertificateCode}").FontSize(10).FontColor(Colors.Grey.Darken1);
                    col.Item().AlignCenter().Text($"Verify at {verifyUrl}").FontSize(9).FontColor(Colors.Grey.Darken1);
                });
        })).GeneratePdf();
    }
}
