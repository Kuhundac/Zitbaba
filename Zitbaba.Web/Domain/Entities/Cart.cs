namespace Zitbaba.Web.Domain.Entities;

public class Cart
{
    public Guid CartId { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }

    /// <summary>The seller whose products are in this cart. Null while the cart is empty.
    /// This is what enforces one seller per cart.</summary>
    public Guid? SellerProfileId { get; set; }
    public SellerProfile? SellerProfile { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();
}
