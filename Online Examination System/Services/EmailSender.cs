using System.Net;
using System.Net.Mail;

namespace Online_Examination_System.Services;

public class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "no-reply@examease.local";
    public string FromName { get; set; } = "ExamEase";
}

public interface IAppEmailSender
{
    /// <summary>Sends an e-mail. Returns false when SMTP is not configured (the mail is written to App_Data/emails instead).</summary>
    Task<bool> SendAsync(string to, string subject, string htmlBody);
}

public class AppEmailSender(Microsoft.Extensions.Options.IOptions<SmtpOptions> options, IWebHostEnvironment env, ILogger<AppEmailSender> log) : IAppEmailSender
{
    public async Task<bool> SendAsync(string to, string subject, string htmlBody)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.Host))
        {
            var dir = Path.Combine(env.ContentRootPath, "App_Data", "emails");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.html");
            await File.WriteAllTextAsync(file, $"<p><b>To:</b> {WebUtility.HtmlEncode(to)}<br><b>Subject:</b> {WebUtility.HtmlEncode(subject)}</p><hr>{htmlBody}");
            log.LogWarning("SMTP is not configured. E-mail to {To} saved to {File}", to, file);
            return false;
        }

        using var client = new SmtpClient(o.Host, o.Port) { EnableSsl = o.EnableSsl };
        if (!string.IsNullOrEmpty(o.UserName)) client.Credentials = new NetworkCredential(o.UserName, o.Password);
        using var msg = new MailMessage(new MailAddress(o.From, o.FromName), new MailAddress(to)) { Subject = subject, Body = htmlBody, IsBodyHtml = true };
        await client.SendMailAsync(msg);
        return true;
    }
}
