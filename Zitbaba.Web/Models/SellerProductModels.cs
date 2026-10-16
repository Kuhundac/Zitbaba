using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Domain.Rules;

namespace Zitbaba.Web.Models;

public record SellerProductInput(
    string Name,
    int CategoryId,
    string? Description,
    decimal Price,
    int QuantityAvailable,
    ProductCondition Condition,
    int? LowStockThreshold);

public enum ProductOutcome { Ok, NotFound, NotAllowed, Invalid, Conflict }

public enum ProductAction { Deactivate, Reactivate, Remove }

public record ProductResult(ProductOutcome Outcome, string? Error = null, Guid? ProductId = null)
{
    public bool Succeeded => Outcome == ProductOutcome.Ok;
    public static ProductResult Ok(Guid? id = null) => new(ProductOutcome.Ok, null, id);
    public static ProductResult Fail(ProductOutcome outcome, string error) => new(outcome, error, null);
}

public class SellerProductFormModel
{
    public Guid ProductId { get; set; }

    [Required, StringLength(160, MinimumLength = 2)]
    [Display(Name = "Product name")]
    public string Name { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Choose a category.")]
    [Display(Name = "Category")]
    public int CategoryId { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    [Range(typeof(decimal), "0", "1000000", ErrorMessage = "Price must be between 0 and 1,000,000.")]
    [Display(Name = "Price (ZMW)")]
    public decimal Price { get; set; }

    [Range(0, 100000, ErrorMessage = "Quantity must be between 0 and 100,000.")]
    [Display(Name = "Quantity in stock")]
    public int QuantityAvailable { get; set; }

    public ProductCondition Condition { get; set; } = ProductCondition.New;

    [Range(0, 100000)]
    [Display(Name = "Low-stock alert at")]
    public int? LowStockThreshold { get; set; }

    /// <summary>Concurrency token, round-tripped as base64.</summary>
    public string? RowVersion { get; set; }

    [BindNever]
    public List<SelectListItem> Categories { get; set; } = new();

    [BindNever]
    public List<SelectListItem> Conditions { get; } = new()
    {
        new SelectListItem("New", nameof(ProductCondition.New)),
        new SelectListItem("Like new", nameof(ProductCondition.LikeNew)),
        new SelectListItem("Used", nameof(ProductCondition.Used))
    };
}

public record SellerProductRow(
    Guid ProductId,
    string Name,
    string CategoryName,
    decimal Price,
    int QuantityAvailable,
    ProductStatus Status,
    bool IsLowStock,
    string? ThumbnailUrl)
{
    public bool CanEdit => ProductRules.CanEdit(Status);
    public bool CanDeactivate => ProductRules.CanDeactivate(Status);
    public bool CanReactivate => ProductRules.CanReactivate(Status);
    public bool CanRemove => ProductRules.CanRemove(Status);
}

public record SellerProductListModel(IReadOnlyList<SellerProductRow> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public record LowStockItem(Guid ProductId, string Name, int QuantityAvailable);

public class SellerDashboardModel
{
    public int ActiveCount { get; set; }
    public int DraftCount { get; set; }
    public int InactiveCount { get; set; }
    public int PendingModerationCount { get; set; }
    public int LowStockCount { get; set; }
    public IReadOnlyList<LowStockItem> LowStockItems { get; set; } = Array.Empty<LowStockItem>();
}

public static class ProductStatusBadges
{
    public static StatusBadgeViewModel For(ProductStatus status) => status switch
    {
        ProductStatus.Active => new StatusBadgeViewModel("Active", BadgeKind.Success),
        ProductStatus.Draft => new StatusBadgeViewModel("Draft", BadgeKind.Info),
        ProductStatus.PendingModeration => new StatusBadgeViewModel("Pending review", BadgeKind.Warning),
        ProductStatus.Inactive => new StatusBadgeViewModel("Inactive", BadgeKind.Warning),
        ProductStatus.Removed => new StatusBadgeViewModel("Removed", BadgeKind.Error),
        ProductStatus.Blocked => new StatusBadgeViewModel("Blocked", BadgeKind.Error),
        _ => new StatusBadgeViewModel(status.ToString())
    };
}
