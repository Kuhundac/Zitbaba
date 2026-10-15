using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Domain.Entities;

public class SellerProfile
{
    public Guid SellerProfileId { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }

    public string StoreName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? StoreImageUrl { get; set; }

    public SellerApplicationStatus ApplicationStatus { get; set; } = SellerApplicationStatus.Pending;
    public string? ApplicationNotes { get; set; }

    public Guid? ReviewedByUserId { get; set; }
    public ApplicationUser? ReviewedByUser { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewReason { get; set; }

    public decimal RatingAverage { get; set; }
    public int RatingCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Product> Products { get; set; } = new List<Product>();
}
