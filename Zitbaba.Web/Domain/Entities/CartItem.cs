namespace Zitbaba.Web.Domain.Entities;

public class CartItem
{
    public int CartItemId { get; set; }

    public Guid CartId { get; set; }
    public Cart? Cart { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public int Quantity { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
