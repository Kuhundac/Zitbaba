using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Domain.Rules;

public enum OrderActor
{
    Buyer,
    Seller
}

/// <summary>The ONE place that says which order status changes are allowed (BR-005).</summary>
public static class OrderRules
{
    public static bool CanTransition(OrderStatus from, OrderStatus to, OrderActor actor) =>
        actor switch
        {
            OrderActor.Seller => (from, to) switch
            {
                (OrderStatus.Pending, OrderStatus.Accepted) => true,
                (OrderStatus.Pending, OrderStatus.Rejected) => true,
                (OrderStatus.Accepted, OrderStatus.ReadyForPickup) => true,
                (OrderStatus.ReadyForPickup, OrderStatus.Completed) => true,
                _ => false
            },
            OrderActor.Buyer => (from, to) switch
            {
                // The buyer may cancel until the order is ready for pickup.
                (OrderStatus.Pending, OrderStatus.CancelledByBuyer) => true,
                (OrderStatus.Accepted, OrderStatus.CancelledByBuyer) => true,
                _ => false
            },
            _ => false
        };

    /// <summary>The statuses this actor may move the order to, in enum order.</summary>
    public static IReadOnlyList<OrderStatus> NextStatuses(OrderStatus from, OrderActor actor) =>
        Enum.GetValues<OrderStatus>().Where(to => CanTransition(from, to, actor)).ToList();

    public static bool IsFinal(OrderStatus status) =>
        status is OrderStatus.Rejected or OrderStatus.Completed or OrderStatus.CancelledByBuyer;

    /// <summary>Rejecting or cancelling puts the reserved stock back (BR-006).</summary>
    public static bool RestoresStock(OrderStatus to) =>
        to is OrderStatus.Rejected or OrderStatus.CancelledByBuyer;

    /// <summary>A seller must explain a rejection so the buyer knows why.</summary>
    public static bool RequiresReason(OrderStatus to) => to == OrderStatus.Rejected;

    /// <summary>Completing a cash pickup means the cash was received.</summary>
    public static bool MarksPaid(OrderStatus to) => to == OrderStatus.Completed;
}
