using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Domain.Rules;
using Zitbaba.Web.Models;

namespace Zitbaba.Web.Services;

public interface IProductImageService
{
    Task<ImageCheck> ValidateAsync(ImageUpload upload, CancellationToken ct = default);
    Task<ProductResult> SetImageAsync(Guid userId, Guid productId, ImageUpload upload, CancellationToken ct = default);
    Task<ProductResult> RemoveImageAsync(Guid userId, Guid productId, CancellationToken ct = default);
    Task<string?> GetImageUrlAsync(Guid userId, Guid productId);
}

public class ProductImageService : IProductImageService
{
    private const string NotApprovedMessage = "You need an approved seller account to do this.";

    private readonly ZitbabaDbContext _db;
    private readonly IAuditService _audit;
    private readonly IImageStorage _storage;

    public ProductImageService(ZitbabaDbContext db, IAuditService audit, IImageStorage storage)
    {
        _db = db;
        _audit = audit;
        _storage = storage;
    }

    public Task<ImageCheck> ValidateAsync(ImageUpload upload, CancellationToken ct = default) =>
        ImageValidator.CheckAsync(upload, ct);

    public async Task<ProductResult> SetImageAsync(Guid userId, Guid productId, ImageUpload upload, CancellationToken ct = default)
    {
        var sellerId = await ApprovedSellerIdAsync(userId);
        if (sellerId is null) return ProductResult.Fail(ProductOutcome.NotAllowed, NotApprovedMessage);

        var product = await FindOwnedAsync(userId, sellerId.Value, productId);
        if (product is null) return ProductResult.Fail(ProductOutcome.NotFound, "Product not found.");

        if (!ProductRules.CanEdit(product.Status))
            return ProductResult.Fail(ProductOutcome.Invalid, "Removed or blocked products cannot be edited.");

        var check = await ImageValidator.CheckAsync(upload, ct);
        if (!check.Ok) return ProductResult.Fail(ProductOutcome.Invalid, check.Error!);

        // 1) store the new file, 2) commit the database change, 3) only then delete the old file(s).
        var url = await _storage.SaveAsync(upload.Content, check.Extension!, ct);

        var existing = await _db.ProductImages.Where(i => i.ProductId == productId).ToListAsync(ct);
        _db.ProductImages.RemoveRange(existing);
        _db.ProductImages.Add(new ProductImage
        {
            ProductId = productId,
            ImageUrl = url,
            ThumbnailUrl = url,
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow
        });
        product.UpdatedAt = DateTime.UtcNow;

        _audit.Stage(userId, "ProductImageChanged", nameof(Product), productId.ToString(),
            new { productId });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            await _storage.DeleteAsync(url, ct);
            return ProductResult.Fail(ProductOutcome.Conflict, "The image could not be saved. Please try again.");
        }

        foreach (var old in existing) await _storage.DeleteAsync(old.ImageUrl, ct);
        return ProductResult.Ok(productId);
    }

    public async Task<ProductResult> RemoveImageAsync(Guid userId, Guid productId, CancellationToken ct = default)
    {
        var sellerId = await ApprovedSellerIdAsync(userId);
        if (sellerId is null) return ProductResult.Fail(ProductOutcome.NotAllowed, NotApprovedMessage);

        var product = await FindOwnedAsync(userId, sellerId.Value, productId);
        if (product is null) return ProductResult.Fail(ProductOutcome.NotFound, "Product not found.");

        var existing = await _db.ProductImages.Where(i => i.ProductId == productId).ToListAsync(ct);
        if (existing.Count == 0) return ProductResult.Ok(productId);

        _db.ProductImages.RemoveRange(existing);
        product.UpdatedAt = DateTime.UtcNow;
        _audit.Stage(userId, "ProductImageRemoved", nameof(Product), productId.ToString(), new { productId });

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return ProductResult.Fail(ProductOutcome.Conflict, "The image could not be removed. Please try again.");
        }

        foreach (var old in existing) await _storage.DeleteAsync(old.ImageUrl, ct);
        return ProductResult.Ok(productId);
    }

    public Task<string?> GetImageUrlAsync(Guid userId, Guid productId) =>
        _db.ProductImages.AsNoTracking()
            .Where(i => i.ProductId == productId && i.Product!.SellerProfile!.UserId == userId)
            .OrderBy(i => i.SortOrder)
            .Select(i => i.ImageUrl)
            .FirstOrDefaultAsync();

    private async Task<Guid?> ApprovedSellerIdAsync(Guid userId)
    {
        return await _db.SellerProfiles.AsNoTracking()
            .Where(s => s.UserId == userId && s.ApplicationStatus == SellerApplicationStatus.Approved)
            .Select(s => (Guid?)s.SellerProfileId)
            .FirstOrDefaultAsync();
    }

    private async Task<Product?> FindOwnedAsync(Guid userId, Guid sellerId, Guid productId)
    {
        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.ProductId == productId && p.SellerProfileId == sellerId);
        if (product is not null) return product;

        if (await _db.Products.AnyAsync(p => p.ProductId == productId))
        {
            await _audit.WriteAsync(userId, "AccessDenied", nameof(Product), productId.ToString(),
                new { productId, area = "Seller" });
        }
        return null;
    }
}
