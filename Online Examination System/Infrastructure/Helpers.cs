using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace Online_Examination_System.Infrastructure;

public interface IPaged
{
    int Page { get; }
    int TotalPages { get; }
    int TotalCount { get; }
    int FirstItem { get; }
    int LastItem { get; }
}

public class PagedResult<T> : IPaged
{
    public required List<T> Items { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public int FirstItem => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int LastItem => Math.Min(Page * PageSize, TotalCount);
}

public static class Extensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, int page, int pageSize = 10)
    {
        pageSize = Math.Clamp(pageSize, 5, 100);
        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, pages);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedResult<T> { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }

    public static string UserId(this ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(Models.Roles.Admin);

    public static string DisplayName(this Enum value) =>
        value.GetType().GetMember(value.ToString()).FirstOrDefault()?.GetCustomAttribute<DisplayAttribute>()?.Name ?? value.ToString();

    public static string FormatBytes(this long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.#} MB"
    };

    public static string Initials(this string? name)
    {
        var parts = (name ?? "?").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Take(2).Select(p => char.ToUpperInvariant(p[0])));
    }

    public static string Truncate(this string? s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s ?? "" : s[..max] + "…";

    public static string FormatDuration(this TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{t.Minutes}m {t.Seconds}s";
}

public static class OwnershipExtensions
{
    /// <summary>Admins see everything; teachers only what they created.</summary>
    public static IQueryable<Models.Exam> OwnedBy(this IQueryable<Models.Exam> q, ClaimsPrincipal user)
    {
        if (user.IsAdmin()) return q;
        var uid = user.UserId();
        return q.Where(e => e.CreatedById == uid);
    }

    public static bool CanManage(this ClaimsPrincipal user, Models.Exam e) => user.IsAdmin() || e.CreatedById == user.UserId();
}

public static class Toast
{
    public const string Success = "ToastSuccess";
    public const string Error = "ToastError";
}
