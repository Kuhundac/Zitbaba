using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Models;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

public class ProductImageTests
{
    private static readonly byte[] PngBytes =
        { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0, 0, 0 };

    private static readonly byte[] WebpBytes =
        { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50, 0, 0, 0, 0 };

    private static ImageUpload Upload(string name, byte[] bytes) =>
        new(name, bytes.Length, new MemoryStream(bytes));

    private static async Task<(Guid UserId, Guid ProductId)> SeedAsync(
        IServiceScope scope, SellerApplicationStatus status = SellerApplicationStatus.Approved, string suffix = "A")
    {
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var userId = Guid.NewGuid();
        var profile = new SellerProfile { UserId = userId, StoreName = "Store " + suffix, ApplicationStatus = status };
        var category = new Category { Name = "Category " + suffix };
        db.SellerProfiles.Add(profile);
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var product = new Product
        {
            SellerProfileId = profile.SellerProfileId,
            CategoryId = category.CategoryId,
            Name = "Calculator " + suffix,
            Price = 10m,
            QuantityAvailable = 1,
            Status = ProductStatus.Active
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return (userId, product.ProductId);
    }

    private static InMemoryImageStorage Storage(IServiceScope scope) =>
        (InMemoryImageStorage)scope.ServiceProvider.GetRequiredService<IImageStorage>();

    [Fact]
    public async Task Fake_jpg_that_is_really_text_is_rejected()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (userId, productId) = await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<IProductImageService>();

        var bytes = Encoding.UTF8.GetBytes("this is just text, not a picture");
        var result = await svc.SetImageAsync(userId, productId, Upload("photo.jpg", bytes));

        Assert.False(result.Succeeded);
        Assert.Empty(Storage(scope).Files);
    }

    [Fact]
    public async Task Svg_is_rejected()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (userId, productId) = await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<IProductImageService>();

        var bytes = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
        var result = await svc.SetImageAsync(userId, productId, Upload("logo.svg", bytes));

        Assert.False(result.Succeeded);
        Assert.Empty(Storage(scope).Files);
    }

    [Fact]
    public async Task File_over_5_MB_is_rejected()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (userId, productId) = await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<IProductImageService>();

        var tooBig = new ImageUpload("big.png", ImageValidator.MaxBytes + 1, new MemoryStream(PngBytes));
        var result = await svc.SetImageAsync(userId, productId, tooBig);

        Assert.False(result.Succeeded);
        Assert.Empty(Storage(scope).Files);
    }

    [Fact]
    public async Task Png_bytes_with_a_jpg_extension_are_rejected()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (userId, productId) = await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<IProductImageService>();

        var result = await svc.SetImageAsync(userId, productId, Upload("photo.jpg", PngBytes));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Valid_png_is_accepted_and_stored_as_the_only_image()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (userId, productId) = await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<IProductImageService>();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();

        var result = await svc.SetImageAsync(userId, productId, Upload("photo.png", PngBytes));

        Assert.True(result.Succeeded);
        var image = await db.ProductImages.SingleAsync();
        Assert.Equal(0, image.SortOrder);
        Assert.Equal(image.ImageUrl, image.ThumbnailUrl);
        Assert.EndsWith(".png", image.ImageUrl);
        Assert.True(Storage(scope).Files.ContainsKey(image.ImageUrl));
    }

    [Fact]
    public async Task Replacing_the_image_deletes_the_old_file_and_keeps_one_row()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (userId, productId) = await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<IProductImageService>();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();

        await svc.SetImageAsync(userId, productId, Upload("a.png", PngBytes));
        var firstUrl = (await db.ProductImages.SingleAsync()).ImageUrl;

        var second = await svc.SetImageAsync(userId, productId, Upload("b.webp", WebpBytes));

        Assert.True(second.Succeeded);
        var image = await db.ProductImages.SingleAsync();
        Assert.NotEqual(firstUrl, image.ImageUrl);
        Assert.False(Storage(scope).Files.ContainsKey(firstUrl));
        Assert.True(Storage(scope).Files.ContainsKey(image.ImageUrl));
    }

    [Fact]
    public async Task Removing_the_image_deletes_the_row_and_the_file()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (userId, productId) = await SeedAsync(scope);
        var svc = scope.ServiceProvider.GetRequiredService<IProductImageService>();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();

        await svc.SetImageAsync(userId, productId, Upload("a.png", PngBytes));
        var result = await svc.RemoveImageAsync(userId, productId);

        Assert.True(result.Succeeded);
        Assert.Empty(await db.ProductImages.ToListAsync());
        Assert.Empty(Storage(scope).Files);
    }

    [Fact]
    public async Task Another_sellers_product_returns_NotFound_and_writes_an_AccessDenied_row()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (_, productOfA) = await SeedAsync(scope, suffix: "A");
        var (userB, _) = await SeedAsync(scope, suffix: "B");
        var svc = scope.ServiceProvider.GetRequiredService<IProductImageService>();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();

        var result = await svc.SetImageAsync(userB, productOfA, Upload("a.png", PngBytes));

        Assert.Equal(ProductOutcome.NotFound, result.Outcome);
        Assert.Empty(Storage(scope).Files);
        Assert.Contains(await db.AuditLogs.ToListAsync(),
            a => a.Action == "AccessDenied" && a.EntityId == productOfA.ToString());
    }

    [Fact]
    public async Task Pending_seller_cannot_upload_an_image()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (userId, productId) = await SeedAsync(scope, SellerApplicationStatus.Pending);
        var svc = scope.ServiceProvider.GetRequiredService<IProductImageService>();

        var result = await svc.SetImageAsync(userId, productId, Upload("a.png", PngBytes));

        Assert.Equal(ProductOutcome.NotAllowed, result.Outcome);
        Assert.Empty(Storage(scope).Files);
    }
}
