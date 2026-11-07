using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Online_Examination_System.Data;
using Online_Examination_System.Infrastructure;
using Online_Examination_System.Models;

namespace Online_Examination_System.Pages.Notifications;

public class IndexModel(ApplicationDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "p")] public int PageNo { get; set; } = 1;
    public PagedResult<Notification> Items { get; set; } = null!;

    public async Task OnGetAsync()
    {
        var uid = User.UserId();
        Items = await db.Notifications.Where(n => n.UserId == uid).OrderByDescending(n => n.Id).AsNoTracking().ToPagedAsync(PageNo, 20);
    }

    /// <summary>Marks a notification as read and follows its link.</summary>
    public async Task<IActionResult> OnGetOpenAsync(long id)
    {
        var uid = User.UserId();
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid);
        if (n is null) return NotFound();
        n.IsRead = true;
        await db.SaveChangesAsync();
        return !string.IsNullOrEmpty(n.Link) && Url.IsLocalUrl(n.Link) ? LocalRedirect(n.Link) : RedirectToPage();
    }

    public async Task<IActionResult> OnPostReadAllAsync()
    {
        var uid = User.UserId();
        await db.Notifications.Where(n => n.UserId == uid && !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostClearAsync()
    {
        var uid = User.UserId();
        await db.Notifications.Where(n => n.UserId == uid && n.IsRead).ExecuteDeleteAsync();
        TempData[Toast.Success] = "Read notifications cleared.";
        return RedirectToPage();
    }
}
