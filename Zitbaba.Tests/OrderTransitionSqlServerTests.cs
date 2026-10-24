using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

/// <summary>Real SQL Server tests: status races and stock restore exactly once.</summary>
public class OrderTransitionSqlServerTests
{
    private sealed record Placed(Guid OrderId, Guid BuyerId, SeededSeller Seller, Guid ProductId);

    /// <summary>Stock 5, buyer takes 2, so 3 are left after the order is placed.</summary>
    private static async Task<Placed> PlaceAsync()
    {
        await using var db = await SqlServerTestDb.NewContextAsync();
        var seller = await SqlServerTestDb.SeedSellerAsync(db);
        var product = await SqlServerTestDb.SeedProductAsync(db, seller, 100m, 5, "Lamp");
        var buyer = await SqlServerTestDb.SeedUserAsync(db);
        Assert.Equal(CartOutcome.Ok, (await new CartService(db).AddAsync(buyer, product.ProductId, 2)).Outcome);
        var result = await new CheckoutService(db, new AuditService(db)).PlaceOrderAsync(buyer, Guid.NewGuid(), 200m);
        Assert.Equal(CheckoutOutcome.Placed, result.Outcome);
        return new Placed(result.OrderId!.Value, buyer, seller, product.ProductId);
    }

    private static async Task<TransitionResult> RunAsync(Func<IOrderTransitionService, Task<TransitionResult>> action)
    {
        await using var db = await SqlServerTestDb.NewContextAsync();
        return await action(new OrderTransitionService(db, new AuditService(db)));
    }

    private static async Task<(OrderStatus Status, int Stock, int History)> ReadAsync(Placed p)
    {
        await using var db = await SqlServerTestDb.NewContextAsync();
        var status = (await db.Orders.AsNoTracking().SingleAsync(o => o.OrderId == p.OrderId)).Status;
        var stock = (await db.Products.AsNoTracking().SingleAsync(x => x.ProductId == p.ProductId)).QuantityAvailable;
        var history = await db.OrderStatusHistory.CountAsync(h => h.OrderId == p.OrderId);
        return (status, stock, history);
    }

    [SqlServerFact]
    public async Task Seller_accepting_while_buyer_cancels_leaves_a_consistent_order()
    {
        for (var round = 0; round < 10; round++)
        {
            var p = await PlaceAsync();

            var results = await Task.WhenAll(
                RunAsync(s => s.SellerAcceptAsync(p.Seller.SellerUserId, p.OrderId)),
                RunAsync(s => s.BuyerCancelAsync(p.BuyerId, p.OrderId, null)));

            var ok = results.Count(r => r.Succeeded);
            var state = await ReadAsync(p);

            Assert.True(ok >= 1);
            Assert.True(state.Status is OrderStatus.Accepted or OrderStatus.CancelledByBuyer);
            Assert.Equal(1 + ok, state.History);
            // Stock is back exactly when the order ended up cancelled, never twice.
            Assert.Equal(state.Status == OrderStatus.CancelledByBuyer ? 5 : 3, state.Stock);
        }
    }

    [SqlServerFact]
    public async Task Two_simultaneous_cancels_restore_stock_once()
    {
        for (var round = 0; round < 5; round++)
        {
            var p = await PlaceAsync();

            var results = await Task.WhenAll(
                RunAsync(s => s.BuyerCancelAsync(p.BuyerId, p.OrderId, null)),
                RunAsync(s => s.BuyerCancelAsync(p.BuyerId, p.OrderId, null)));

            var state = await ReadAsync(p);
            Assert.Equal(1, results.Count(r => r.Succeeded));
            Assert.Equal(OrderStatus.CancelledByBuyer, state.Status);
            Assert.Equal(5, state.Stock);
            Assert.Equal(2, state.History);
        }
    }

    [SqlServerFact]
    public async Task Reject_racing_a_cancel_has_one_winner_and_restores_stock_once()
    {
        for (var round = 0; round < 5; round++)
        {
            var p = await PlaceAsync();

            var results = await Task.WhenAll(
                RunAsync(s => s.SellerRejectAsync(p.Seller.SellerUserId, p.OrderId, "Sold elsewhere")),
                RunAsync(s => s.BuyerCancelAsync(p.BuyerId, p.OrderId, null)));

            var state = await ReadAsync(p);
            Assert.Equal(1, results.Count(r => r.Succeeded));
            Assert.True(state.Status is OrderStatus.Rejected or OrderStatus.CancelledByBuyer);
            Assert.Equal(5, state.Stock);
            Assert.Equal(2, state.History);
        }
    }
}
