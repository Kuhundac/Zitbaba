using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Domain.Entities;

/// <summary>Who changed an order's status, when and why (FR-042).</summary>
public class OrderStatusHistory
{
    public int OrderStatusHistoryId { get; set; }

    public Guid OrderId { get; set; }
    public Order? Order { get; set; }

    public OrderStatus? FromStatus { get; set; }
    public OrderStatus ToStatus { get; set; }

    /// <summary>No foreign key on purpose, like AuditLog: history must survive user changes.</summary>
    public Guid? ActorUserId { get; set; }

    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
