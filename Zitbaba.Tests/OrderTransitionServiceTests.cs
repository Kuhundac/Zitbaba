using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

public class OrderTransitionServiceTests
{
    private sealed record Ctx(
        ZitbabaDbContext Db, ICartService Cart, ICheckoutService Checkout, IOrderTransitionService Orders);

    private sealed record Placed(Guid OrderId, Guid BuyerId, SellerProfile Seller, Product Product);

    private static Ctx Resolve(IServiceScope scope) => new(
        scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>(),
        scope.ServiceProvider.GetRequiredService<ICartService>(),
        scope.ServiceProvider.GetRequiredService<ICheckoutService>(),
        scope.ServiceProvider.GetRequiredService<IOrderTransitionService>());

    /// <summary>Stock 5, buyer takes 2, so 3 are left after the order is placed.</summary>
    private static async Task<Placed> PlaceAsync(Ctx c)
    {
        var seller = new SellerProfile
        {
            UserId = Guid.NewGuid(),
            StoreName = "Store A",
            ApplicationStatus = SellerApplicationStatus.Approved
        };
        c.Db.SellerProfiles.Add(seller);
        await c.Db.SaveChangesAsync();

        var product = new Product
        {
            SellerProfileId = seller.SellerProfileId,
            CategoryId = 1,
            Name = "Lamp",
            Price = 100m,
            QuantityAvailable = 5,
            Status = ProductStatus.Active
        };
        c.Db.Products.Add(product);
        await c.Db.SaveChangesAsync();

        var buyer = Guid.NewGuid();
        await c.Cart.AddAsync(buyer, product.ProductId, 2);
        var result = await c.Checkout.PlaceOrderAsync(buyer, Guid.NewGuid(), 200m);
        Assert.Equal(CheckoutOutcome.Placed, result.Outcome);
        Assert.Equal(3, await StockAsync(c, product.ProductId));

        return new Placed(result.OrderId!.Value, buyer, seller, product);
    }

    private static async Task<int> StockAsync(Ctx c, Guid productId) =>
        (await c.Db.Products.AsNoTracking().SingleAsync(p => p.ProductId == productId)).QuantityAvailable;

    private static Task<Order> OrderAsync(Ctx c, Guid orderId) =>
        c.Db.Orders.AsNoTracking().SingleAsync(o => o.OrderId == orderId);

    private static Task<List<OrderStatusHistory>> HistoryAsync(Ctx c, Guid orderId) =>
        c.Db.OrderStatusHistory.AsNoTracking()
            .Where(h => h.OrderId == orderId).OrderBy(h => h.OrderStatusHistoryId).ToListAsync();

    [Fact]
    public async Task Seller_accepts_a_pending_order_with_history_and_audit()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var p = await PlaceAsync(c);

        var result = await c.Orders.SellerAcceptAsync(p.Seller.UserId, p.OrderId);

        Assert.Equal(TransitionOutcome.Ok, result.Outcome);
        Assert.Equal(OrderStatus.Accepted, result.NewStatus);
        Assert.Equal(OrderStatus.Accepted, (await OrderAsync(c, p.OrderId)).Status);

        var history = await HistoryAsync(c, p.OrderId);
        Assert.Equal(2, history.Count);
        Assert.Equal(OrderStatus.Pending, history[1].FromStatus);
        Assert.Equal(OrderStatus.Accepted, history[1].ToStatus);
        Assert.Equal(p.Seller.UserId, history[1].ActorUserId);
        Assert.True(await c.Db.AuditLogs.AnyAsync(a =>
            a.Action == "OrderAccepted" && a.EntityId == p.OrderId.ToString()));
    }

    [Fact]
    public async Task Happy_path_accept_ready_complete_marks_payment_paid()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var p = await PlaceAsync(c);

        Assert.True((await c.Orders.SellerAcceptAsync(p.Seller.UserId, p.OrderId)).Succeeded);
        Assert.True((await c.Orders.SellerMarkReadyAsync(p.Seller.UserId, p.OrderId)).Succeeded);
        Assert.Equal(PaymentStatus.Pending, (await OrderAsync(c, p.OrderId)).PaymentStatus);
        Assert.True((await c.Orders.SellerCompleteAsync(p.Seller.UserId, p.OrderId)).Succeeded);

        var order = await OrderAsync(c, p.OrderId);
        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(4, (await HistoryAsync(c, p.OrderId)).Count);
        Assert.Equal(3, await StockAsync(c, p.Product.ProductId));
    }

    [Fact]
    public async Task Reject_without_a_reason_is_refused()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var p = await PlaceAsync(c);

        var none = await c.Orders.SellerRejectAsync(p.Seller.UserId, p.OrderId, null);
        var blank = await c.Orders.SellerRejectAsync(p.Seller.UserId, p.OrderId, "   ");

        Assert.Equal(TransitionOutcome.ReasonRequired, none.Outcome);
        Assert.Equal(TransitionOutcome.ReasonRequired, blank.Outcome);
        Assert.Equal(OrderStatus.Pending, (await OrderAsync(c, p.OrderId)).Status);
        Assert.Equal(3, await StockAsync(c, p.Product.ProductId));
    }

    [Fact]
    public async Task Reject_with_a_reason_restores_stock_and_records_the_reason()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var p = await PlaceAsync(c);

        var result = await c.Orders.SellerRejectAsync(p.Seller.UserId, p.OrderId, "  Item sold elsewhere  ");

        Assert.Equal(TransitionOutcome.Ok, result.Outcome);
        Assert.Equal(OrderStatus.Rejected, (await OrderAsync(c, p.OrderId)).Status);
        Assert.Equal(5, await StockAsync(c, p.Product.ProductId));
        var last = (await HistoryAsync(c, p.OrderId)).Last();
        Assert.Equal("Item sold elsewhere", last.Reason);
        Assert.True(await c.Db.AuditLogs.AnyAsync(a =>
            a.Action == "OrderRejected" && a.EntityId == p.OrderId.ToString()));
    }

    [Fact]
    public async Task Buyer_cancels_a_pending_order_and_stock_comes_back()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var p = await PlaceAsync(c);

        var result = await c.Orders.BuyerCancelAsync(p.BuyerId, p.OrderId, "Changed my mind");

        Assert.Equal(TransitionOutcome.Ok, result.Outcome);
        Assert.Equal(OrderStatus.CancelledByBuyer, (await OrderAsync(c, p.OrderId)).Status);
        Assert.Equal(5, await StockAsync(c, p.Product.ProductId));
        var last = (await HistoryAsync(c, p.OrderId)).Last();
        Assert.Equal(p.BuyerId, last.ActorUserId);
        Assert.Equal("Changed my mind", last.Reason);
        Assert.True(await c.Db.AuditLogs.AnyAsync(a =>
            a.Action == "OrderCancelled" && a.EntityId == p.OrderId.ToString()));
    }

    [Fact]
    public async Task Buyer_can_cancel_an_accepted_order_but_not_one_that_is_ready()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);

        var accepted = await PlaceAsync(c);
        await c.Orders.SellerAcceptAsync(accepted.Seller.UserId, accepted.OrderId);
        var cancel = await c.Orders.BuyerCancelAsync(accepted.BuyerId, accepted.OrderId, null);
        Assert.Equal(TransitionOutcome.Ok, cancel.Outcome);
        Assert.Equal(5, await StockAsync(c, accepted.Product.ProductId));

        var ready = await PlaceAsync(c);
        await c.Orders.SellerAcceptAsync(ready.Seller.UserId, ready.OrderId);
        await c.Orders.SellerMarkReadyAsync(ready.Seller.UserId, ready.OrderId);
        var refused = await c.Orders.BuyerCancelAsync(ready.BuyerId, ready.OrderId, null);
        Assert.Equal(TransitionOutcome.NotAllowed, refused.Outcome);
        Assert.Equal(OrderStatus.ReadyForPickup, (await OrderAsync(c, ready.OrderId)).Status);
        Assert.Equal(3, await StockAsync(c, ready.Product.ProductId));
    }

    [Fact]
    public async Task Moves_that_skip_a_step_or_repeat_are_refused()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var p = await PlaceAsync(c);

        Assert.Equal(TransitionOutcome.NotAllowed,
            (await c.Orders.SellerCompleteAsync(p.Seller.UserId, p.OrderId)).Outcome);
        Assert.Equal(TransitionOutcome.NotAllowed,
            (await c.Orders.SellerMarkReadyAsync(p.Seller.UserId, p.OrderId)).Outcome);

        Assert.True((await c.Orders.SellerAcceptAsync(p.Seller.UserId, p.OrderId)).Succeeded);
        Assert.Equal(TransitionOutcome.NotAllowed,
            (await c.Orders.SellerAcceptAsync(p.Seller.UserId, p.OrderId)).Outcome);

        Assert.Equal(OrderStatus.Accepted, (await OrderAsync(c, p.OrderId)).Status);
        Assert.Equal(2, (await HistoryAsync(c, p.OrderId)).Count);
    }

    [Fact]
    public async Task Another_seller_cannot_change_the_order_and_is_audited()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var p = await PlaceAsync(c);
        var stranger = Guid.NewGuid();

        var result = await c.Orders.SellerAcceptAsync(stranger, p.OrderId);

        Assert.Equal(TransitionOutcome.NotFound, result.Outcome);
        Assert.Equal(OrderStatus.Pending, (await OrderAsync(c, p.OrderId)).Status);
        Assert.True(await c.Db.AuditLogs.AnyAsync(a =>
            a.Action == "AccessDenied" && a.ActorUserId == stranger && a.EntityId == p.OrderId.ToString()));
    }

    [Fact]
    public async Task Another_buyer_cannot_cancel_the_order_and_is_audited()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var p = await PlaceAsync(c);
        var stranger = Guid.NewGuid();

        var result = await c.Orders.BuyerCancelAsync(stranger, p.OrderId, null);

        Assert.Equal(TransitionOutcome.NotFound, result.Outcome);
        Assert.Equal(OrderStatus.Pending, (await OrderAsync(c, p.OrderId)).Status);
        Assert.Equal(3, await StockAsync(c, p.Product.ProductId));
        Assert.True(await c.Db.AuditLogs.AnyAsync(a =>
            a.Action == "AccessDenied" && a.ActorUserId == stranger && a.EntityId == p.OrderId.ToString()));
    }

    [Fact]
    public async Task A_finished_order_cannot_restore_stock_a_second_time()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var p = await PlaceAsync(c);

        Assert.True((await c.Orders.SellerRejectAsync(p.Seller.UserId, p.OrderId, "Sold out")).Succeeded);
        var again = await c.Orders.BuyerCancelAsync(p.BuyerId, p.OrderId, null);

        Assert.Equal(TransitionOutcome.NotAllowed, again.Outcome);
        Assert.Equal(OrderStatus.Rejected, (await OrderAsync(c, p.OrderId)).Status);
        Assert.Equal(5, await StockAsync(c, p.Product.ProductId));
    }
}
