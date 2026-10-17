using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Domain.Entities;

public class Order
{
    public Guid OrderId { get; set; } = Guid.CreateVersion7();

    /// <summary>Human-friendly unique number, e.g. ZB-20261017-A3F9K2.</summary>
    public string OrderNumber { get; set; } = string.Empty;

    public Guid BuyerUserId { get; set; }
    public ApplicationUser? Buyer { get; set; }

    public Guid SellerProfileId { get; set; }
    public SellerProfile? SellerProfile { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
    public FulfilmentMethod FulfilmentMethod { get; set; } = FulfilmentMethod.Pickup;

    public decimal TotalAmount { get; set; }

    /// <summary>One-time key from the checkout form. Unique per buyer, so a retried or
    /// double-clicked submit returns the same order instead of creating another.</summary>
    public Guid SubmissionKey { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<OrderStatusHistory> History { get; set; } = new List<OrderStatusHistory>();
}
