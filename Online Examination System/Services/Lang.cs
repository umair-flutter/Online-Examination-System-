using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Html;

namespace Online_Examination_System.Services;

public record LanguageInfo(string Code, string Name, string NativeName, bool IsRtl, string Flag);

/// <summary>
/// Lightweight JSON based translator. English text is the key; each language has
/// Resources/Lang/{code}.json mapping English → translation. Missing keys fall back to English.
/// Inject into views with <c>@inject Lang L</c> and write <c>@L["Dashboard"]</c>.
/// </summary>
public class Lang
{
    public static readonly LanguageInfo[] Supported =
    [
        new("en", "English", "English", false, "🇬🇧"),
        new("ar", "Arabic", "العربية", true, "🇸🇦"),
        new("ur", "Urdu", "اردو", true, "🇵🇰"),
        new("fr", "French", "Français", false, "🇫🇷"),
    ];

    public const string DefaultCulture = "en";
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> _cache = new();
    private readonly string _root;

    public Lang(IWebHostEnvironment env) => _root = Path.Combine(env.ContentRootPath, "Resources", "Lang");

    public static LanguageInfo Current =>
        Supported.FirstOrDefault(l => l.Code == CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) ?? Supported[0];

    public static bool IsRtl => Current.IsRtl;

    public string this[string key] => Translate(key);

    public string this[string key, params object[] args] => string.Format(Translate(key), args);

    /// <summary>Translated text as raw HTML (use only with trusted keys that contain markup).</summary>
    public IHtmlContent Html(string key) => new HtmlString(Translate(key));

    public string Translate(string key)
    {
        var code = Current.Code;
        if (code == DefaultCulture) return key;
        var dict = _cache.GetOrAdd(code, Load);
        return dict.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : key;
    }

    private IReadOnlyDictionary<string, string> Load(string code)
    {
        var file = Path.Combine(_root, $"{code}.json");
        if (!File.Exists(file)) return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? new();
        }
        catch (JsonException) { return new Dictionary<string, string>(); }
    }
}
