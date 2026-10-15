using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Domain.Entities;

public class Product
{
    public Guid ProductId { get; set; } = Guid.CreateVersion7();

    public Guid SellerProfileId { get; set; }
    public SellerProfile? SellerProfile { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public decimal Price { get; set; }
    public int QuantityAvailable { get; set; }
    public ProductCondition Condition { get; set; } = ProductCondition.New;
    public ProductStatus Status { get; set; } = ProductStatus.Draft;
    public int? LowStockThreshold { get; set; }

    public int ViewCount { get; set; }
    public int SaveCount { get; set; }

    // Optimistic concurrency token. Day 4 uses this for stock safety.
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
}
