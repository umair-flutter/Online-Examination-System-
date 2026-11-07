using ClosedXML.Excel;
using Online_Examination_System.Models;

namespace Online_Examination_System.Services;

public record ImportResult(int Imported, List<string> Errors);

public record QuestionImportRow(int Row, string SubjectCode, QuestionType Type, string Text, double Marks, Difficulty Difficulty,
    List<string> Choices, string Correct, string? Explanation, string? Tags);

public record UserImportRow(int Row, string FullName, string UserName, string Email, string Role, string Password, string? StudentNumber, string? ClassName);

public class ExcelService
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static readonly string[] Letters = ["A", "B", "C", "D", "E", "F"];

    /// <summary>Generic table export.</summary>
    public byte[] Export(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName.Length > 31 ? sheetName[..31] : sheetName);
        for (var i = 0; i < headers.Count; i++) ws.Cell(1, i + 1).Value = headers[i];
        var header = ws.Range(1, 1, 1, headers.Count);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#4f46e5");
        header.Style.Font.FontColor = XLColor.White;

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Count; c++) ws.Cell(r, c + 1).Value = ToCell(row[c]);
            r++;
        }
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Min(r, 200), 8, 60);
        return Save(wb);
    }

    private static XLCellValue ToCell(object? v) => v switch
    {
        null => Blank.Value,
        DateTime d => d,
        bool b => b ? "Yes" : "No",
        int i => i,
        long l => l,
        double d => d,
        decimal m => m,
        Enum e => e.ToString(),
        _ => v.ToString()
    };

    private static byte[] Save(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ---------------- Questions ----------------
    public static readonly string[] QuestionHeaders =
        ["SubjectCode", "Type", "Question", "Marks", "Difficulty", "ChoiceA", "ChoiceB", "ChoiceC", "ChoiceD", "ChoiceE", "ChoiceF", "Correct", "Explanation", "Tags"];

    public byte[] QuestionTemplate()
    {
        var rows = new List<IReadOnlyList<object?>>
        {
            new object?[] { "MATH101", "MultipleChoice", "What is 5 + 7?", 1, "Easy", "10", "11", "12", "13", null, null, "C", "5 + 7 = 12", "arithmetic" },
            new object?[] { "MATH101", "MultipleAnswer", "Which numbers are prime?", 2, "Medium", "2", "4", "7", "9", null, null, "A,C", null, "primes" },
            new object?[] { "SCI101", "TrueFalse", "Water boils at 100°C at sea level.", 1, "Easy", null, null, null, null, null, null, "True", null, null },
            new object?[] { "ENG101", "ShortAnswer", "Past tense of 'go'?", 1, "Easy", null, null, null, null, null, null, "went", "Separate several accepted answers with |", null },
            new object?[] { "ENG101", "Essay", "Describe your favourite book.", 5, "Hard", null, null, null, null, null, null, null, null, null },
        };
        using var wb = new XLWorkbook(new MemoryStream(Export("Questions", QuestionHeaders, rows)));
        var help = wb.Worksheets.Add("Instructions");
        string[] lines =
        [
            "Type: MultipleChoice | TrueFalse | ShortAnswer | Essay | MultipleAnswer",
            "Difficulty: Easy | Medium | Hard",
            "Correct: letter of the correct choice (e.g. C). MultipleAnswer: letters separated by comma (A,C).",
            "TrueFalse: True or False. ShortAnswer: accepted answers separated by | . Essay: leave empty.",
            "SubjectCode must match an existing subject code."
        ];
        for (var i = 0; i < lines.Length; i++) help.Cell(i + 1, 1).Value = lines[i];
        help.Column(1).Width = 110;
        return Save(wb);
    }

    public byte[] ExportQuestions(IEnumerable<Question> questions) =>
        Export("Questions", QuestionHeaders, questions.Select(q =>
        {
            var choices = q.Choices.OrderBy(c => c.Order).ToList();
            string correct = q.Type switch
            {
                QuestionType.TrueFalse => choices.FirstOrDefault(c => c.IsCorrect)?.Text ?? "",
                QuestionType.ShortAnswer => q.CorrectShortAnswer ?? "",
                QuestionType.Essay => "",
                _ => string.Join(",", choices.Select((c, i) => (c, i)).Where(x => x.c.IsCorrect).Select(x => Letters[Math.Min(x.i, 5)]))
            };
            var cells = new List<object?> { q.Subject.Code, q.Type.ToString(), q.Text, q.Marks, q.Difficulty.ToString() };
            for (var i = 0; i < 6; i++) cells.Add(q.Type != QuestionType.TrueFalse && i < choices.Count ? choices[i].Text : null);
            cells.AddRange([correct, q.Explanation, q.Tags]);
            return (IReadOnlyList<object?>)cells;
        }));

    public (List<QuestionImportRow> Rows, List<string> Errors) ReadQuestions(Stream stream)
    {
        var rows = new List<QuestionImportRow>();
        var errors = new List<string>();
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var n = row.RowNumber();
            string S(int col) => row.Cell(col).GetString().Trim();
            if (string.IsNullOrWhiteSpace(S(3))) continue;

            if (!Enum.TryParse<QuestionType>(S(2).Replace(" ", "").Replace("/", ""), true, out var type))
            { errors.Add($"Row {n}: unknown type '{S(2)}'."); continue; }
            if (!double.TryParse(S(4), out var marks) || marks <= 0) marks = 1;
            if (!Enum.TryParse<Difficulty>(S(5), true, out var diff)) diff = Difficulty.Medium;
            var choices = Enumerable.Range(6, 6).Select(S).Where(s => s.Length > 0).ToList();
            var correct = S(12);

            if (type is QuestionType.MultipleChoice or QuestionType.MultipleAnswer)
            {
                if (choices.Count < 2) { errors.Add($"Row {n}: at least two choices are required."); continue; }
                var letters = correct.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (letters.Length == 0 || letters.Any(l => Array.IndexOf(Letters, l.ToUpperInvariant()) is var i && (i < 0 || i >= choices.Count)))
                { errors.Add($"Row {n}: 'Correct' must contain valid choice letters."); continue; }
                if (type == QuestionType.MultipleChoice && letters.Length != 1)
                { errors.Add($"Row {n}: multiple choice questions need exactly one correct letter."); continue; }
            }
            if (type == QuestionType.TrueFalse && !bool.TryParse(correct, out _))
            { errors.Add($"Row {n}: 'Correct' must be True or False."); continue; }
            if (type == QuestionType.ShortAnswer && correct.Length == 0)
            { errors.Add($"Row {n}: short answer questions need at least one accepted answer."); continue; }

            rows.Add(new QuestionImportRow(n, S(1), type, S(3), marks, diff, choices, correct, NullIfEmpty(S(13)), NullIfEmpty(S(14))));
        }
        return (rows, errors);
    }

    public static List<QuestionChoice> BuildChoices(QuestionImportRow r)
    {
        if (r.Type == QuestionType.TrueFalse)
        {
            var isTrue = bool.Parse(r.Correct);
            return [new() { Text = "True", IsCorrect = isTrue, Order = 0 }, new() { Text = "False", IsCorrect = !isTrue, Order = 1 }];
        }
        if (r.Type is QuestionType.ShortAnswer or QuestionType.Essay) return [];
        var correct = r.Correct.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => Array.IndexOf(Letters, l.ToUpperInvariant())).ToHashSet();
        return r.Choices.Select((t, i) => new QuestionChoice { Text = t, IsCorrect = correct.Contains(i), Order = i }).ToList();
    }

    // ---------------- Users ----------------
    public static readonly string[] UserHeaders = ["FullName", "UserName", "Email", "Role", "Password", "StudentNumber", "Class"];

    public byte[] UserTemplate() => Export("Users", UserHeaders,
    [
        new object?[] { "John Smith", "john", "john@example.com", "Student", "User@123", "S-1001", "Class 10-A" },
        new object?[] { "Jane Doe", "jane", "jane@example.com", "Teacher", "User@123", null, null },
    ]);

    public (List<UserImportRow> Rows, List<string> Errors) ReadUsers(Stream stream)
    {
        var rows = new List<UserImportRow>();
        var errors = new List<string>();
        using var wb = new XLWorkbook(stream);
        foreach (var row in wb.Worksheets.First().RowsUsed().Skip(1))
        {
            var n = row.RowNumber();
            string S(int col) => row.Cell(col).GetString().Trim();
            if (S(2).Length == 0 && S(3).Length == 0) continue;
            var role = Roles.All.FirstOrDefault(r => r.Equals(S(4), StringComparison.OrdinalIgnoreCase));
            if (S(2).Length == 0 || S(3).Length == 0) { errors.Add($"Row {n}: UserName and Email are required."); continue; }
            if (role is null) { errors.Add($"Row {n}: Role must be Admin, Teacher or Student."); continue; }
            rows.Add(new UserImportRow(n, S(1).Length > 0 ? S(1) : S(2), S(2), S(3), role, S(5).Length > 0 ? S(5) : "User@123", NullIfEmpty(S(6)), NullIfEmpty(S(7))));
        }
        return (rows, errors);
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;
}
