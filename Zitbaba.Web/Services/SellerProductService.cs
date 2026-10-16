using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Domain.Rules;
using Zitbaba.Web.Models;

namespace Zitbaba.Web.Services;

public interface ISellerProductService
{
    Task<SellerDashboardModel> GetDashboardAsync(Guid userId);
    Task<SellerProductListModel> ListAsync(Guid userId, int page);
    Task<SellerProductFormModel?> GetFormAsync(Guid userId, Guid productId);
    Task<IReadOnlyList<CategoryOption>> GetCategoryOptionsAsync(int? includeCategoryId);
    Task<ProductResult> CreateAsync(Guid userId, SellerProductInput input);
    Task<ProductResult> UpdateAsync(Guid userId, Guid productId, SellerProductInput input, string? rowVersion);
    Task<ProductResult> SetStatusAsync(Guid userId, Guid productId, ProductAction action);
}

public class SellerProductService : ISellerProductService
{
    public const int DefaultLowStockThreshold = 3;
    public const int PageSize = 10;

    private const string NotApprovedMessage = "You need an approved seller account to do this.";
    private const string ConflictMessage =
        "This product was changed somewhere else. Reload the page to see the latest version, then try again.";

    private readonly ZitbabaDbContext _db;
    private readonly IAuditService _audit;
    private readonly IConfiguration _config;

    public SellerProductService(ZitbabaDbContext db, IAuditService audit, IConfiguration config)
    {
        _db = db;
        _audit = audit;
        _config = config;
    }

    private bool RequireModeration => _config.GetValue<bool>("Marketplace:RequireListingModeration");

    // ---------- Reads ----------

    public async Task<SellerDashboardModel> GetDashboardAsync(Guid userId)
    {
        var sid = await ApprovedSellerIdAsync(userId);
        if (sid is null) return new SellerDashboardModel();
        var sellerId = sid.Value;

        var counts = await _db.Products.AsNoTracking()
            .Where(p => p.SellerProfileId == sellerId)
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        int CountOf(ProductStatus s) => counts.Where(c => c.Status == s).Select(c => c.Count).FirstOrDefault();

        var low = _db.Products.AsNoTracking().Where(p =>
            p.SellerProfileId == sellerId
            && p.Status != ProductStatus.Removed
            && p.Status != ProductStatus.Blocked
            && p.QuantityAvailable <= (p.LowStockThreshold ?? DefaultLowStockThreshold));

        var lowCount = await low.CountAsync();
        var lowItems = await low.OrderBy(p => p.QuantityAvailable).ThenBy(p => p.Name)
            .Take(5)
            .Select(p => new LowStockItem(p.ProductId, p.Name, p.QuantityAvailable))
            .ToListAsync();

        return new SellerDashboardModel
        {
            ActiveCount = CountOf(ProductStatus.Active),
            DraftCount = CountOf(ProductStatus.Draft),
            InactiveCount = CountOf(ProductStatus.Inactive),
            PendingModerationCount = CountOf(ProductStatus.PendingModeration),
            LowStockCount = lowCount,
            LowStockItems = lowItems
        };
    }

    public async Task<SellerProductListModel> ListAsync(Guid userId, int page)
    {
        var sid = await ApprovedSellerIdAsync(userId);
        if (sid is null) return new SellerProductListModel(Array.Empty<SellerProductRow>(), 1, PageSize, 0);
        var sellerId = sid.Value;

        var query = _db.Products.AsNoTracking()
            .Where(p => p.SellerProfileId == sellerId && p.Status != ProductStatus.Removed);

        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, pages);

        var items = await query
            .OrderByDescending(p => p.UpdatedAt).ThenBy(p => p.Name)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(p => new SellerProductRow(
                p.ProductId,
                p.Name,
                p.Category!.Name,
                p.Price,
                p.QuantityAvailable,
                p.Status,
                p.QuantityAvailable <= (p.LowStockThreshold ?? DefaultLowStockThreshold),
                p.Images.OrderBy(i => i.SortOrder).Select(i => i.ThumbnailUrl ?? i.ImageUrl).FirstOrDefault()))
            .ToListAsync();

        return new SellerProductListModel(items, page, PageSize, total);
    }

    public async Task<SellerProductFormModel?> GetFormAsync(Guid userId, Guid productId)
    {
        var sid = await ApprovedSellerIdAsync(userId);
        if (sid is null) return null;

        var product = await FindOwnedAsync(userId, sid.Value, productId);
        if (product is null || !ProductRules.CanEdit(product.Status)) return null;

        return new SellerProductFormModel
        {
            ProductId = product.ProductId,
            Name = product.Name,
            CategoryId = product.CategoryId,
            Description = product.Description,
            Price = product.Price,
            QuantityAvailable = product.QuantityAvailable,
            Condition = product.Condition,
            LowStockThreshold = product.LowStockThreshold,
            RowVersion = Convert.ToBase64String(product.RowVersion)
        };
    }

    public async Task<IReadOnlyList<CategoryOption>> GetCategoryOptionsAsync(int? includeCategoryId)
    {
        var rows = await _db.Categories.AsNoTracking()
            .Where(c => c.IsActive || c.CategoryId == includeCategoryId)
            .Select(c => new
            {
                c.CategoryId,
                c.Name,
                c.IsActive,
                ParentName = c.ParentCategory != null ? c.ParentCategory.Name : null
            })
            .ToListAsync();

        return rows
            .Select(c => new CategoryOption(
                c.CategoryId,
                c.ParentName is null ? c.Name : c.ParentName + " > " + c.Name,
                c.IsActive))
            .OrderBy(o => o.Name)
            .ToList();
    }

    // ---------- Writes ----------

    public async Task<ProductResult> CreateAsync(Guid userId, SellerProductInput input)
    {
        var sid = await ApprovedSellerIdAsync(userId);
        if (sid is null) return ProductResult.Fail(ProductOutcome.NotAllowed, NotApprovedMessage);

        var error = await ValidateAsync(input, null);
        if (error is not null) return ProductResult.Fail(ProductOutcome.Invalid, error);

        var product = new Product
        {
            SellerProfileId = sid.Value,
            CategoryId = input.CategoryId,
            Name = input.Name.Trim(),
            Description = Clean(input.Description),
            Price = input.Price,
            QuantityAvailable = input.QuantityAvailable,
            Condition = input.Condition,
            LowStockThreshold = input.LowStockThreshold,
            Status = ProductRules.InitialStatus(RequireModeration),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        _db.Products.Add(product);

        _audit.Stage(userId, "ProductCreated", nameof(Product), product.ProductId.ToString(),
            new { productId = product.ProductId, categoryId = product.CategoryId, status = product.Status.ToString() });

        await _db.SaveChangesAsync();
        return ProductResult.Ok(product.ProductId);
    }

    public async Task<ProductResult> UpdateAsync(Guid userId, Guid productId, SellerProductInput input, string? rowVersion)
    {
        var sid = await ApprovedSellerIdAsync(userId);
        if (sid is null) return ProductResult.Fail(ProductOutcome.NotAllowed, NotApprovedMessage);

        var product = await FindOwnedAsync(userId, sid.Value, productId);
        if (product is null) return ProductResult.Fail(ProductOutcome.NotFound, "Product not found.");

        if (!ProductRules.CanEdit(product.Status))
            return ProductResult.Fail(ProductOutcome.Invalid, "Removed or blocked products cannot be edited.");

        // An unchanged category is allowed even if an admin has deactivated it since.
        var error = await ValidateAsync(input, product.CategoryId);
        if (error is not null) return ProductResult.Fail(ProductOutcome.Invalid, error);

        if (!string.IsNullOrEmpty(rowVersion))
        {
            try
            {
                _db.Entry(product).Property(p => p.RowVersion).OriginalValue = Convert.FromBase64String(rowVersion);
            }
            catch (FormatException)
            {
                return ProductResult.Fail(ProductOutcome.Invalid, "The form is out of date. Reload the page and try again.");
            }
        }

        product.Name = input.Name.Trim();
        product.CategoryId = input.CategoryId;
        product.Description = Clean(input.Description);
        product.Price = input.Price;
        product.QuantityAvailable = input.QuantityAvailable;
        product.Condition = input.Condition;
        product.LowStockThreshold = input.LowStockThreshold;
        product.UpdatedAt = DateTime.UtcNow;

        _audit.Stage(userId, "ProductUpdated", nameof(Product), product.ProductId.ToString(),
            new { productId = product.ProductId });

        return await SaveAsync(product.ProductId);
    }

    public async Task<ProductResult> SetStatusAsync(Guid userId, Guid productId, ProductAction action)
    {
        var sid = await ApprovedSellerIdAsync(userId);
        if (sid is null) return ProductResult.Fail(ProductOutcome.NotAllowed, NotApprovedMessage);

        var product = await FindOwnedAsync(userId, sid.Value, productId);
        if (product is null) return ProductResult.Fail(ProductOutcome.NotFound, "Product not found.");

        var from = product.Status;
        ProductStatus to;
        switch (action)
        {
            case ProductAction.Deactivate:
                if (!ProductRules.CanDeactivate(from))
                    return ProductResult.Fail(ProductOutcome.Invalid, "Only active products can be deactivated.");
                to = ProductStatus.Inactive;
                break;
            case ProductAction.Reactivate:
                if (!ProductRules.CanReactivate(from))
                    return ProductResult.Fail(ProductOutcome.Invalid, "Only inactive products can be reactivated.");
                to = ProductRules.InitialStatus(RequireModeration);
                break;
            case ProductAction.Remove:
                if (!ProductRules.CanRemove(from))
                    return ProductResult.Fail(ProductOutcome.Invalid, "This product can no longer be removed.");
                to = ProductStatus.Removed;
                break;
            default:
                return ProductResult.Fail(ProductOutcome.Invalid, "Unknown action.");
        }

        product.Status = to;
        product.UpdatedAt = DateTime.UtcNow;

        _audit.Stage(userId, "ProductStatusChanged", nameof(Product), product.ProductId.ToString(),
            new { productId = product.ProductId, from = from.ToString(), to = to.ToString() });

        return await SaveAsync(product.ProductId);
    }

    // ---------- Helpers ----------

    private async Task<Guid?> ApprovedSellerIdAsync(Guid userId)
    {
        return await _db.SellerProfiles.AsNoTracking()
            .Where(s => s.UserId == userId && s.ApplicationStatus == SellerApplicationStatus.Approved)
            .Select(s => (Guid?)s.SellerProfileId)
            .FirstOrDefaultAsync();
    }

    /// <summary>Loads a product only if it belongs to this seller. If it exists but belongs to
    /// someone else, an AccessDenied audit row is written and null is returned (callers answer 404).</summary>
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

    private async Task<string?> ValidateAsync(SellerProductInput input, int? allowInactiveCategoryId)
    {
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length < 2 || name.Length > 160) return "Product name must be 2 to 160 characters.";

        var description = Clean(input.Description);
        if (description is { Length: > 2000 }) return "Description can be at most 2000 characters.";

        if (input.Price < 0) return "Price cannot be negative.";
        if (input.Price > 1_000_000m) return "Price cannot be more than ZMW 1,000,000.00.";
        if (decimal.Round(input.Price, 2) != input.Price) return "Price can have at most 2 decimal places.";

        if (input.QuantityAvailable < 0) return "Quantity cannot be negative.";
        if (input.QuantityAvailable > 100_000) return "Quantity cannot be more than 100,000.";

        if (input.LowStockThreshold is < 0 or > 100_000)
            return "Low-stock alert must be between 0 and 100,000.";

        if (!Enum.IsDefined(input.Condition)) return "Choose a valid condition.";

        var categoryOk = await _db.Categories.AnyAsync(c =>
            c.CategoryId == input.CategoryId && (c.IsActive || c.CategoryId == allowInactiveCategoryId));
        if (!categoryOk) return "Choose an active category.";

        return null;
    }

    private async Task<ProductResult> SaveAsync(Guid productId)
    {
        try
        {
            await _db.SaveChangesAsync();
            return ProductResult.Ok(productId);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ProductResult.Fail(ProductOutcome.Conflict, ConflictMessage);
        }
    }

    private static string? Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
