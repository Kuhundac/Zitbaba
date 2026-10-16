using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

/// <summary>Test double: keeps "files" in memory so tests never touch the disk.</summary>
internal sealed class InMemoryImageStorage : IImageStorage
{
    public Dictionary<string, byte[]> Files { get; } = new();

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        var url = "/uploads/products/" + Guid.NewGuid().ToString("N") + extension;
        Files[url] = buffer.ToArray();
        return url;
    }

    public Task DeleteAsync(string imageUrl, CancellationToken ct = default)
    {
        Files.Remove(imageUrl);
        return Task.CompletedTask;
    }
}
