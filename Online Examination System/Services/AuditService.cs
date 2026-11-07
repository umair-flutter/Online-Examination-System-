using System.Security.Claims;
using Online_Examination_System.Data;
using Online_Examination_System.Models;

namespace Online_Examination_System.Services;

public class AuditService(ApplicationDbContext db, IHttpContextAccessor http)
{
    public async Task LogAsync(string action, string? entity = null, object? entityId = null, string? details = null, string? userName = null)
    {
        var ctx = http.HttpContext;
        db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            Entity = entity,
            EntityId = entityId?.ToString(),
            Details = details?.Length > 2000 ? details[..2000] : details,
            UserId = ctx?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            UserName = userName ?? ctx?.User.Identity?.Name,
            IpAddress = ctx?.Connection.RemoteIpAddress?.ToString()
        });
        await db.SaveChangesAsync();
    }
}
