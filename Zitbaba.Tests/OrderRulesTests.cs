using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Domain.Rules;

namespace Zitbaba.Tests;

public class OrderRulesTests
{
    [Fact]
    public void Allowed_moves_are_exactly_the_documented_ones()
    {
        var allowed = new HashSet<(OrderStatus, OrderStatus, OrderActor)>
        {
            (OrderStatus.Pending, OrderStatus.Accepted, OrderActor.Seller),
            (OrderStatus.Pending, OrderStatus.Rejected, OrderActor.Seller),
            (OrderStatus.Accepted, OrderStatus.ReadyForPickup, OrderActor.Seller),
            (OrderStatus.ReadyForPickup, OrderStatus.Completed, OrderActor.Seller),
            (OrderStatus.Pending, OrderStatus.CancelledByBuyer, OrderActor.Buyer),
            (OrderStatus.Accepted, OrderStatus.CancelledByBuyer, OrderActor.Buyer)
        };

        var mismatches = new List<string>();
        foreach (var from in Enum.GetValues<OrderStatus>())
        foreach (var to in Enum.GetValues<OrderStatus>())
        foreach (var actor in Enum.GetValues<OrderActor>())
        {
            var expected = allowed.Contains((from, to, actor));
            if (OrderRules.CanTransition(from, to, actor) != expected)
                mismatches.Add(actor + ": " + from + " -> " + to + " should be " + expected);
        }

        Assert.True(mismatches.Count == 0, string.Join("; ", mismatches));
    }

    [Fact]
    public void Final_states_have_no_next_moves_for_anyone()
    {
        foreach (var status in new[] { OrderStatus.Rejected, OrderStatus.Completed, OrderStatus.CancelledByBuyer })
        {
            Assert.True(OrderRules.IsFinal(status));
            Assert.Empty(OrderRules.NextStatuses(status, OrderActor.Seller));
            Assert.Empty(OrderRules.NextStatuses(status, OrderActor.Buyer));
        }

        Assert.False(OrderRules.IsFinal(OrderStatus.Pending));
        Assert.False(OrderRules.IsFinal(OrderStatus.Accepted));
        Assert.False(OrderRules.IsFinal(OrderStatus.ReadyForPickup));
    }

    [Fact]
    public void Next_statuses_per_actor()
    {
        Assert.Equal(
            new[] { OrderStatus.Accepted, OrderStatus.Rejected },
            OrderRules.NextStatuses(OrderStatus.Pending, OrderActor.Seller));
        Assert.Equal(
            new[] { OrderStatus.CancelledByBuyer },
            OrderRules.NextStatuses(OrderStatus.Pending, OrderActor.Buyer));

        Assert.Equal(
            new[] { OrderStatus.ReadyForPickup },
            OrderRules.NextStatuses(OrderStatus.Accepted, OrderActor.Seller));
        Assert.Equal(
            new[] { OrderStatus.CancelledByBuyer },
            OrderRules.NextStatuses(OrderStatus.Accepted, OrderActor.Buyer));

        Assert.Equal(
            new[] { OrderStatus.Completed },
            OrderRules.NextStatuses(OrderStatus.ReadyForPickup, OrderActor.Seller));
        Assert.Empty(OrderRules.NextStatuses(OrderStatus.ReadyForPickup, OrderActor.Buyer));
    }

    [Fact]
    public void Only_reject_and_cancel_restore_stock()
    {
        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            var expected = status == OrderStatus.Rejected || status == OrderStatus.CancelledByBuyer;
            Assert.Equal(expected, OrderRules.RestoresStock(status));
        }
    }

    [Fact]
    public void Only_reject_requires_a_reason()
    {
        foreach (var status in Enum.GetValues<OrderStatus>())
            Assert.Equal(status == OrderStatus.Rejected, OrderRules.RequiresReason(status));
    }

    [Fact]
    public void Only_complete_marks_payment_paid()
    {
        foreach (var status in Enum.GetValues<OrderStatus>())
            Assert.Equal(status == OrderStatus.Completed, OrderRules.MarksPaid(status));
    }
}
