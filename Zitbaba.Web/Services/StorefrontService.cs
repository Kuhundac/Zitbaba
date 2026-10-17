using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Services;

// Public store page data. No phone, room number, email or application notes.
public record StorefrontModel(
    CatalogueSellerInfo Seller,
    string? Description,
    PagedResult<CatalogueItem> Products);

public interface IStorefrontService
{
    /// <summary>Returns null unless the seller is Approved.</summary>
    Task<StorefrontModel?> GetAsync(Guid sellerProfileId, int page);
}

public class StorefrontService : IStorefrontService
{
    private readonly ZitbabaDbContext _db;
    private readonly ICatalogueService _catalogue;

    public StorefrontService(ZitbabaDbContext db, ICatalogueService catalogue)
    {
        _db = db;
        _catalogue = catalogue;
    }

    public async Task<StorefrontModel?> GetAsync(Guid sellerProfileId, int page)
    {
        var seller = await _db.SellerProfiles.AsNoTracking()
            .Where(s => s.SellerProfileId == sellerProfileId
                && s.ApplicationStatus == SellerApplicationStatus.Approved)
            .Select(s => new
            {
                s.SellerProfileId,
                s.StoreName,
                s.Description,
                s.RatingAverage,
                s.RatingCount,
                s.CreatedAt
            })
            .FirstOrDefaultAsync();

        if (seller is null) return null;

        var products = await _catalogue.SearchAsync(
            new CatalogueQuery(Sort: CatalogueSort.Newest, Page: page, SellerId: sellerProfileId));

        return new StorefrontModel(
            new CatalogueSellerInfo(
                seller.SellerProfileId, seller.StoreName,
                seller.RatingCount > 0 ? seller.RatingAverage : null,
                seller.RatingCount, seller.CreatedAt),
            seller.Description,
            products);
    }
}
