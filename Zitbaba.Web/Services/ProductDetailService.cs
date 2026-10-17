using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Services;

// Public seller info only: no phone, room number, email or user id.
public record CatalogueSellerInfo(
    Guid SellerProfileId, string StoreName, decimal? RatingAverage, int RatingCount, DateTime MemberSince);

public record CatalogueProductDetail(
    Guid ProductId,
    string Name,
    string? Description,
    decimal Price,
    ProductCondition Condition,
    int CategoryId,
    string CategoryName,
    string? ParentCategoryName,
    CatalogueStock Stock,
    int QuantityAvailable,
    IReadOnlyList<string> ImageUrls,
    CatalogueSellerInfo Seller);

public interface IProductDetailService
{
    /// <summary>Returns null unless the product is Active and its seller is Approved.</summary>
    Task<CatalogueProductDetail?> GetAsync(Guid productId);
}

public class ProductDetailService : IProductDetailService
{
    private readonly ZitbabaDbContext _db;

    public ProductDetailService(ZitbabaDbContext db) => _db = db;

    public async Task<CatalogueProductDetail?> GetAsync(Guid productId)
    {
        var row = await _db.Products.AsNoTracking()
            .Where(p => p.ProductId == productId
                && p.Status == ProductStatus.Active
                && p.SellerProfile!.ApplicationStatus == SellerApplicationStatus.Approved)
            .Select(p => new
            {
                p.ProductId,
                p.Name,
                p.Description,
                p.Price,
                p.Condition,
                p.CategoryId,
                CategoryName = p.Category!.Name,
                ParentCategoryName = p.Category!.ParentCategory != null ? p.Category!.ParentCategory!.Name : null,
                p.QuantityAvailable,
                p.LowStockThreshold,
                ImageUrls = p.Images.OrderBy(i => i.SortOrder).Select(i => i.ImageUrl).ToList(),
                p.SellerProfileId,
                StoreName = p.SellerProfile!.StoreName,
                p.SellerProfile!.RatingAverage,
                p.SellerProfile!.RatingCount,
                MemberSince = p.SellerProfile!.CreatedAt
            })
            .FirstOrDefaultAsync();

        if (row is null) return null;

        return new CatalogueProductDetail(
            row.ProductId, row.Name, row.Description, row.Price, row.Condition,
            row.CategoryId, row.CategoryName, row.ParentCategoryName,
            CatalogueService.StockOf(row.QuantityAvailable, row.LowStockThreshold),
            row.QuantityAvailable,
            row.ImageUrls,
            new CatalogueSellerInfo(
                row.SellerProfileId, row.StoreName,
                row.RatingCount > 0 ? row.RatingAverage : null,
                row.RatingCount, row.MemberSince));
    }
}
