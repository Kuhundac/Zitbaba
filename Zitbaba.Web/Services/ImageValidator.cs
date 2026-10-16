namespace Zitbaba.Web.Services;

public record ImageUpload(string FileName, long Length, Stream Content);

public record ImageCheck(bool Ok, string? Error, string? Extension)
{
    public static ImageCheck Fail(string error) => new(false, error, null);
    public static ImageCheck Pass(string extension) => new(true, null, extension);
}

public static class ImageValidator
{
    public const long MaxBytes = 5L * 1024 * 1024;

    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".webp" };

    public static async Task<ImageCheck> CheckAsync(ImageUpload? upload, CancellationToken ct = default)
    {
        if (upload is null) return ImageCheck.Fail("Choose an image file.");
        if (upload.Length == 0) return ImageCheck.Fail("The selected file is empty.");
        if (upload.Length > MaxBytes) return ImageCheck.Fail("The image is larger than 5 MB.");

        var extension = Path.GetExtension(upload.FileName ?? string.Empty).ToLowerInvariant();
        if (extension is ".svg" or ".svgz")
            return ImageCheck.Fail("SVG images are not allowed. Use a JPG, PNG or WebP image.");
        if (!AllowedExtensions.Contains(extension))
            return ImageCheck.Fail("Only JPG, PNG or WebP images are allowed.");

        if (!upload.Content.CanRead || !upload.Content.CanSeek)
            return ImageCheck.Fail("Could not read the image. Please try again.");

        // Read the first bytes and compare them with the real file signatures.
        upload.Content.Position = 0;
        var header = new byte[12];
        var total = 0;
        while (total < header.Length)
        {
            var read = await upload.Content.ReadAsync(header.AsMemory(total, header.Length - total), ct);
            if (read == 0) break;
            total += read;
        }
        upload.Content.Position = 0;

        var kind = Detect(header, total);
        if (kind is null) return ImageCheck.Fail("That file is not a real JPG, PNG or WebP image.");

        var expected = extension is ".jpg" or ".jpeg" ? "jpg" : extension.TrimStart('.');
        if (kind != expected) return ImageCheck.Fail("The file contents do not match its extension.");

        return ImageCheck.Pass("." + kind);
    }

    private static string? Detect(byte[] h, int n)
    {
        if (n >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return "jpg";

        if (n >= 8 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47
            && h[4] == 0x0D && h[5] == 0x0A && h[6] == 0x1A && h[7] == 0x0A) return "png";

        if (n >= 12 && h[0] == 0x52 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x46
            && h[8] == 0x57 && h[9] == 0x45 && h[10] == 0x42 && h[11] == 0x50) return "webp";

        return null;
    }
}
