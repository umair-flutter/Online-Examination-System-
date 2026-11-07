namespace Online_Examination_System.Services;

public class FileStorageService(IWebHostEnvironment env)
{
    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".webp"];
    public static readonly string[] AttachmentExtensions =
        [".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".csv", ".zip", ".jpg", ".jpeg", ".png", ".gif", ".webp"];
    public const long MaxImageBytes = 2 * 1024 * 1024;
    public const long MaxAttachmentBytes = 10 * 1024 * 1024;

    public string PrivateRoot => Path.Combine(env.ContentRootPath, "App_Data", "attachments");

    public static string? ValidateImage(IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!ImageExtensions.Contains(ext)) return "Only JPG, PNG, GIF or WEBP images are allowed.";
        if (file.Length > MaxImageBytes) return "Image must be smaller than 2 MB.";
        return null;
    }

    public static string? ValidateAttachment(IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AttachmentExtensions.Contains(ext)) return $"File type {ext} is not allowed.";
        if (file.Length > MaxAttachmentBytes) return "File must be smaller than 10 MB.";
        return null;
    }

    /// <summary>Saves a public image under wwwroot/uploads/{folder} and returns its web path.</summary>
    public async Task<string> SavePublicImageAsync(IFormFile file, string folder)
    {
        var dir = Path.Combine(env.WebRootPath, "uploads", folder);
        Directory.CreateDirectory(dir);
        var name = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
        await using (var fs = File.Create(Path.Combine(dir, name))) await file.CopyToAsync(fs);
        return $"/uploads/{folder}/{name}";
    }

    public void DeletePublicFile(string? webPath)
    {
        if (string.IsNullOrWhiteSpace(webPath) || !webPath.StartsWith("/uploads/")) return;
        var full = Path.GetFullPath(Path.Combine(env.WebRootPath, webPath.TrimStart('/')));
        if (full.StartsWith(Path.GetFullPath(Path.Combine(env.WebRootPath, "uploads"))) && File.Exists(full)) File.Delete(full);
    }

    /// <summary>Saves a private file (outside wwwroot) and returns the stored file name.</summary>
    public async Task<string> SavePrivateAsync(IFormFile file)
    {
        Directory.CreateDirectory(PrivateRoot);
        var name = $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName).ToLowerInvariant()}";
        await using var fs = File.Create(Path.Combine(PrivateRoot, name));
        await file.CopyToAsync(fs);
        return name;
    }

    public string? GetPrivatePath(string storedName)
    {
        var full = Path.GetFullPath(Path.Combine(PrivateRoot, Path.GetFileName(storedName)));
        return File.Exists(full) ? full : null;
    }

    public void DeletePrivate(string storedName)
    {
        var p = GetPrivatePath(storedName);
        if (p != null) File.Delete(p);
    }
}
