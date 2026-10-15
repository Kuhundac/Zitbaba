namespace Zitbaba.Web.Domain.Entities;

public class ProductImage
{
    public int ImageId { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public string ImageUrl { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
