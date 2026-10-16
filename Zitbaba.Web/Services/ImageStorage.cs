using Microsoft.AspNetCore.Hosting;

namespace Zitbaba.Web.Services;

public interface IImageStorage
{
    /// <summary>Stores the stream under a random name and returns its public URL.</summary>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default);

    /// <summary>Deletes a previously stored image by URL. Unknown or unsafe URLs are ignored.</summary>
    Task DeleteAsync(string imageUrl, CancellationToken ct = default);
}

public class LocalImageStorage : IImageStorage
{
    private const string UrlPrefix = "/uploads/products/";
    private static readonly string[] AllowedExtensions = { ".jpg", ".png", ".webp" };

    private readonly string _root;

    public LocalImageStorage(IWebHostEnvironment env)
    {
        var webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        _root = Path.Combine(webRoot, "uploads", "products");
    }

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default)
    {
        extension = extension.ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
            throw new ArgumentException("Unsupported image extension.", nameof(extension));

        Directory.CreateDirectory(_root);
        var fileName = Guid.NewGuid().ToString("N") + extension;
        var path = Path.Combine(_root, fileName);

        await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(file, ct);
        }

        return UrlPrefix + fileName;
    }

    public Task DeleteAsync(string imageUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(imageUrl) || !imageUrl.StartsWith(UrlPrefix, StringComparison.Ordinal))
            return Task.CompletedTask;

        var fileName = imageUrl[UrlPrefix.Length..];
        // Refuse anything that is not a bare file name (no path tricks).
        if (fileName.Length == 0 || fileName != Path.GetFileName(fileName))
            return Task.CompletedTask;

        var path = Path.Combine(_root, fileName);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: an orphaned file is harmless, a crash is not.
        }
        return Task.CompletedTask;
    }
}
