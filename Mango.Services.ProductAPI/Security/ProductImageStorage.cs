using Microsoft.AspNetCore.Http;

namespace Mango.Services.ProductAPI.Security;

public static class ProductImageStorage
{
    public const int MaximumBytes = 2 * 1024 * 1024;

    public static byte[] Validate(IFormFile image)
    {
        if (image.Length <= 0 || image.Length > MaximumBytes)
            throw new InvalidDataException("Choose a PNG, JPEG or WebP image no larger than 2 MB.");
        using var input = image.OpenReadStream();
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + count > MaximumBytes)
                throw new InvalidDataException("The image exceeds 2 MB.");
            output.Write(buffer, 0, count);
        }
        var bytes = output.ToArray();
        var extension = Path.GetExtension(image.FileName).ToLowerInvariant();
        var png = extension == ".png" && bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var jpeg = (extension == ".jpg" || extension == ".jpeg") &&
            bytes.AsSpan().StartsWith(new byte[] { 255, 216, 255 });
        var webp = extension == ".webp" && bytes.Length >= 12 &&
            bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8);
        if (!png && !jpeg && !webp)
            throw new InvalidDataException("The image extension and file signature must match PNG, JPEG or WebP.");
        return bytes;
    }

    public static string Save(IFormFile image, string webRoot)
    {
        var bytes = Validate(image);
        var folder = Path.Combine(webRoot, "ProductImages");
        Directory.CreateDirectory(folder);
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("The image folder cannot be a symbolic link.");
        var fileName = Guid.NewGuid().ToString("N") + Path.GetExtension(image.FileName).ToLowerInvariant();
        var target = Path.Combine(folder, fileName);
        var created = false;
        try
        {
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write);
            created = true;
            output.Write(bytes);
        }
        catch
        {
            if (created && File.Exists(target)) File.Delete(target);
            throw;
        }
        return "/ProductImages/" + fileName;
    }

    public static string? ManagedPath(string? imageUrl, string webRoot)
    {
        if (string.IsNullOrWhiteSpace(imageUrl)) return null;
        var normalized = imageUrl.Replace('\\', '/');
        if (normalized.StartsWith("wwwroot/", StringComparison.Ordinal)) normalized = normalized[7..];
        const string prefix = "/ProductImages/";
        if (!normalized.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var fileName = normalized[prefix.Length..];
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains('/') || fileName.Contains(':') ||
            fileName.Contains("..") || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(fileName) != fileName) return null;
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".webp")) return null;
        var folder = Path.GetFullPath(Path.Combine(webRoot, "ProductImages"));
        if (Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) return null;
        var target = Path.GetFullPath(Path.Combine(folder, fileName));
        return string.Equals(Path.GetDirectoryName(target), folder, StringComparison.OrdinalIgnoreCase) ? target : null;
    }

    public static void DeleteManaged(string? imageUrl, string webRoot)
    {
        var target = ManagedPath(imageUrl, webRoot);
        if (target != null && File.Exists(target)) File.Delete(target);
    }
}
