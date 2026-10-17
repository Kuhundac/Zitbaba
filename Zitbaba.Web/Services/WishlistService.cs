using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Services;

public enum WishlistOutcome
{
    Added,
    AlreadySaved,
    NotAvailable
}

public interface IWishlistService
{
    Task<WishlistOutcome> AddAsync(Guid userId, Guid productId);
    Task<bool> RemoveAsync(Guid userId, Guid productId);
    Task<bool> IsSavedAsync(Guid userId, Guid productId);
    Task<PagedResult<CatalogueItem>> ListAsync(Guid userId, int page);
}

public class WishlistService : IWishlistService
{
    private readonly ZitbabaDbContext _db;

    public WishlistService(ZitbabaDbContext db) => _db = db;

    public async Task<WishlistOutcome> AddAsync(Guid userId, Guid productId)
    {
        var visible = await _db.Products.AsNoTracking().AnyAsync(p =>
            p.ProductId == productId
            && p.Status == ProductStatus.Active
            && p.SellerProfile!.ApplicationStatus == SellerApplicationStatus.Approved);
        if (!visible) return WishlistOutcome.NotAvailable;

        if (await _db.WishlistItems.AnyAsync(w => w.UserId == userId && w.ProductId == productId))
            return WishlistOutcome.AlreadySaved;

        _db.WishlistItems.Add(new WishlistItem
        {
            UserId = userId,
            ProductId = productId,
            CreatedAt = DateTime.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync();
            return WishlistOutcome.Added;
        }
        catch (DbUpdateException)
        {
            // Double click or two tabs: the unique index stopped the second row.
            _db.ChangeTracker.Clear();
            if (await _db.WishlistItems.AnyAsync(w => w.UserId == userId && w.ProductId == productId))
                return WishlistOutcome.AlreadySaved;
            throw;
        }
    }

    public async Task<bool> RemoveAsync(Guid userId, Guid productId)
    {
        var item = await _db.WishlistItems
            .FirstOrDefaultAsync(w => w.UserId == userId && w.ProductId == productId);
        if (item is null) return false;

        _db.WishlistItems.Remove(item);
        await _db.SaveChangesAsync();
        return true;
    }

    public Task<bool> IsSavedAsync(Guid userId, Guid productId) =>
        _db.WishlistItems.AsNoTracking().AnyAsync(w => w.UserId == userId && w.ProductId == productId);

    public async Task<PagedResult<CatalogueItem>> ListAsync(Guid userId, int page)
    {
        // Only products the public may see. Hidden ones stay in the table but are not shown.
        var query = _db.WishlistItems.AsNoTracking().Where(w =>
            w.UserId == userId
            && w.Product!.Status == ProductStatus.Active
            && w.Product.SellerProfile!.ApplicationStatus == SellerApplicationStatus.Approved);

        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)CatalogueService.PageSize));
        page = Math.Clamp(page, 1, pages);

        var rows = await query
            .OrderByDescending(w => w.CreatedAt).ThenBy(w => w.WishlistItemId)
            .Skip((page - 1) * CatalogueService.PageSize).Take(CatalogueService.PageSize)
            .Select(w => new
            {
                w.Product!.ProductId,
                w.Product.Name,
                w.Product.Price,
                w.Product.Condition,
                CategoryName = w.Product.Category!.Name,
                w.Product.SellerProfileId,
                StoreName = w.Product.SellerProfile!.StoreName,
                w.Product.QuantityAvailable,
                w.Product.LowStockThreshold,
                ImageUrl = w.Product.Images.OrderBy(i => i.SortOrder)
                    .Select(i => i.ThumbnailUrl ?? i.ImageUrl).FirstOrDefault()
            })
            .ToListAsync();

        var items = rows.Select(r => new CatalogueItem(
            r.ProductId, r.Name, r.Price, r.Condition, r.CategoryName,
            r.SellerProfileId, r.StoreName,
            CatalogueService.StockOf(r.QuantityAvailable, r.LowStockThreshold),
            r.ImageUrl)).ToList();

        return new PagedResult<CatalogueItem>(items, page, CatalogueService.PageSize, total);
    }
}
