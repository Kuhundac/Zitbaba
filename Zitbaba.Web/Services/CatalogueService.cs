using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Services;

public enum CatalogueSort
{
    Newest = 0,
    PriceLowToHigh = 1,
    PriceHighToLow = 2
}

public enum CatalogueStock
{
    InStock,
    LowStock,
    OutOfStock
}

public record CatalogueQuery(
    string? Keyword = null,
    int? CategoryId = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    ProductCondition? Condition = null,
    bool InStockOnly = false,
    CatalogueSort Sort = CatalogueSort.Newest,
    int Page = 1);

// Public view of a product. No phone number, room number or other private seller data.
public record CatalogueItem(
    Guid ProductId,
    string Name,
    decimal Price,
    ProductCondition Condition,
    string CategoryName,
    Guid SellerProfileId,
    string StoreName,
    CatalogueStock Stock,
    string? ImageUrl);

public record CatalogueCategory(int CategoryId, string Name, IReadOnlyList<CatalogueCategory> Children);

public interface ICatalogueService
{
    Task<PagedResult<CatalogueItem>> SearchAsync(CatalogueQuery query);
    Task<IReadOnlyList<CatalogueItem>> GetRecentAsync(int count);
    Task<IReadOnlyList<CatalogueCategory>> GetCategoriesAsync();
}

public class CatalogueService : ICatalogueService
{
    public const int PageSize = 12;
    private const int MaxKeywordLength = 100;
    private const int MaxTerms = 5;
    private const int MaxRecent = 24;

    private readonly ZitbabaDbContext _db;

    public CatalogueService(ZitbabaDbContext db) => _db = db;

    /// <summary>The ONE rule for what the public may see: Active product from an Approved seller.</summary>
    private IQueryable<Product> VisibleProducts() =>
        _db.Products.AsNoTracking().Where(p =>
            p.Status == ProductStatus.Active
            && p.SellerProfile!.ApplicationStatus == SellerApplicationStatus.Approved);

    public async Task<PagedResult<CatalogueItem>> SearchAsync(CatalogueQuery query)
    {
        var q = VisibleProducts();

        foreach (var term in ParseTerms(query.Keyword))
        {
            var t = term;
            q = q.Where(p =>
                p.Name.ToLower().Contains(t)
                || (p.Description != null && p.Description.ToLower().Contains(t)));
        }

        if (query.CategoryId is int categoryId)
            q = q.Where(p => p.CategoryId == categoryId || p.Category!.ParentCategoryId == categoryId);

        var min = NonNegative(query.MinPrice);
        var max = NonNegative(query.MaxPrice);
        if (min is decimal lo && max is decimal hi && lo > hi)
            (min, max) = (hi, lo);
        if (min is decimal minPrice) q = q.Where(p => p.Price >= minPrice);
        if (max is decimal maxPrice) q = q.Where(p => p.Price <= maxPrice);

        if (query.Condition is ProductCondition condition && Enum.IsDefined(condition))
            q = q.Where(p => p.Condition == condition);

        if (query.InStockOnly)
            q = q.Where(p => p.QuantityAvailable > 0);

        q = query.Sort switch
        {
            CatalogueSort.PriceLowToHigh => q.OrderBy(p => p.Price).ThenBy(p => p.ProductId),
            CatalogueSort.PriceHighToLow => q.OrderByDescending(p => p.Price).ThenBy(p => p.ProductId),
            _ => q.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.ProductId)
        };

        var total = await q.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        var page = Math.Clamp(query.Page, 1, pages);

        var items = await ToItemsAsync(q.Skip((page - 1) * PageSize).Take(PageSize));
        return new PagedResult<CatalogueItem>(items, page, PageSize, total);
    }

    public async Task<IReadOnlyList<CatalogueItem>> GetRecentAsync(int count)
    {
        count = Math.Clamp(count, 1, MaxRecent);
        var q = VisibleProducts()
            .OrderByDescending(p => p.CreatedAt).ThenBy(p => p.ProductId)
            .Take(count);
        return await ToItemsAsync(q);
    }

    public async Task<IReadOnlyList<CatalogueCategory>> GetCategoriesAsync()
    {
        var rows = await _db.Categories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new { c.CategoryId, c.Name, c.ParentCategoryId })
            .ToListAsync();

        return rows
            .Where(r => r.ParentCategoryId is null)
            .Select(r => new CatalogueCategory(
                r.CategoryId,
                r.Name,
                rows.Where(c => c.ParentCategoryId == r.CategoryId)
                    .Select(c => new CatalogueCategory(c.CategoryId, c.Name, Array.Empty<CatalogueCategory>()))
                    .ToList()))
            .ToList();
    }

    public static CatalogueStock StockOf(int quantity, int? lowStockThreshold)
    {
        if (quantity <= 0) return CatalogueStock.OutOfStock;
        var threshold = lowStockThreshold ?? SellerProductService.DefaultLowStockThreshold;
        return quantity <= threshold ? CatalogueStock.LowStock : CatalogueStock.InStock;
    }

    private static async Task<List<CatalogueItem>> ToItemsAsync(IQueryable<Product> q)
    {
        var rows = await q.Select(p => new
        {
            p.ProductId,
            p.Name,
            p.Price,
            p.Condition,
            CategoryName = p.Category!.Name,
            p.SellerProfileId,
            StoreName = p.SellerProfile!.StoreName,
            p.QuantityAvailable,
            p.LowStockThreshold,
            ImageUrl = p.Images.OrderBy(i => i.SortOrder)
                .Select(i => i.ThumbnailUrl ?? i.ImageUrl)
                .FirstOrDefault()
        }).ToListAsync();

        return rows.Select(r => new CatalogueItem(
            r.ProductId, r.Name, r.Price, r.Condition, r.CategoryName,
            r.SellerProfileId, r.StoreName,
            StockOf(r.QuantityAvailable, r.LowStockThreshold),
            r.ImageUrl)).ToList();
    }

    private static List<string> ParseTerms(string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return new List<string>();
        var text = keyword.Trim();
        if (text.Length > MaxKeywordLength) text = text[..MaxKeywordLength];
        return text.ToLowerInvariant()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct()
            .Take(MaxTerms)
            .ToList();
    }

    private static decimal? NonNegative(decimal? value) =>
        value is decimal d && d >= 0m ? d : null;
}
