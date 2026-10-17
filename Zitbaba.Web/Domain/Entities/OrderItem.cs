namespace Zitbaba.Web.Domain.Entities;

/// <summary>A snapshot of the product name and price at purchase time (BR-004).</summary>
public class OrderItem
{
    public int OrderItemId { get; set; }

    public Guid OrderId { get; set; }
    public Order? Order { get; set; }

    public Guid ProductId { get; set; }
    public Product? Product { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}
