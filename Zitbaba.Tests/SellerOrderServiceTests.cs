using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

public class SellerOrderServiceTests
{
    private sealed record Ctx(
        ZitbabaDbContext Db,
        ICartService Cart,
        ICheckoutService Checkout,
        IOrderTransitionService Transitions,
        ISellerOrderService Orders);

    private sealed record Placed(Guid OrderId, ApplicationUser Buyer, SellerProfile Seller);

    private static Ctx Resolve(IServiceScope scope) => new(
        scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>(),
        scope.ServiceProvider.GetRequiredService<ICartService>(),
        scope.ServiceProvider.GetRequiredService<ICheckoutService>(),
        scope.ServiceProvider.GetRequiredService<IOrderTransitionService>(),
        scope.ServiceProvider.GetRequiredService<ISellerOrderService>());

    private static async Task<SellerProfile> SeedSellerAsync(Ctx c, string store = "Store A")
    {
        var seller = new SellerProfile
        {
            UserId = Guid.NewGuid(),
            StoreName = store,
            ApplicationStatus = SellerApplicationStatus.Approved
        };
        c.Db.SellerProfiles.Add(seller);
        await c.Db.SaveChangesAsync();
        return seller;
    }

    private static async Task<Placed> PlaceAsync(Ctx c, SellerProfile seller, string buyerName = "Chanda Mwila")
    {
        var buyer = new ApplicationUser
        {
            UserName = "b" + Guid.NewGuid().ToString("N"),
            FullName = buyerName,
            Phone = "0971234567"
        };
        c.Db.Users.Add(buyer);

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

        await c.Cart.AddAsync(buyer.Id, product.ProductId, 1);
        var result = await c.Checkout.PlaceOrderAsync(buyer.Id, Guid.NewGuid(), 100m);
        Assert.Equal(CheckoutOutcome.Placed, result.Outcome);
        return new Placed(result.OrderId!.Value, buyer, seller);
    }

    [Fact]
    public async Task Pending_count_only_counts_my_pending_orders()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var mine = await SeedSellerAsync(c, "Mine");
        var other = await SeedSellerAsync(c, "Other");

        var first = await PlaceAsync(c, mine);
        await PlaceAsync(c, mine);
        await PlaceAsync(c, other);

        Assert.Equal(2, await c.Orders.CountPendingAsync(mine.UserId));
        Assert.Equal(1, await c.Orders.CountPendingAsync(other.UserId));

        await c.Transitions.SellerAcceptAsync(mine.UserId, first.OrderId);
        Assert.Equal(1, await c.Orders.CountPendingAsync(mine.UserId));
    }

    [Fact]
    public async Task List_filters_follow_the_order_status()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var seller = await SeedSellerAsync(c);

        var toAccept = await PlaceAsync(c, seller);
        var toReject = await PlaceAsync(c, seller);
        await PlaceAsync(c, seller);

        await c.Transitions.SellerAcceptAsync(seller.UserId, toAccept.OrderId);
        await c.Transitions.SellerRejectAsync(seller.UserId, toReject.OrderId, "Sold out");

        var pending = await c.Orders.ListAsync(seller.UserId, SellerOrderFilter.Pending, 1);
        var inProgress = await c.Orders.ListAsync(seller.UserId, SellerOrderFilter.InProgress, 1);
        var closed = await c.Orders.ListAsync(seller.UserId, SellerOrderFilter.Closed, 1);
        var all = await c.Orders.ListAsync(seller.UserId, SellerOrderFilter.All, 1);

        Assert.Equal(1, pending.TotalCount);
        Assert.Equal(1, inProgress.TotalCount);
        Assert.Equal(toAccept.OrderId, inProgress.Items[0].OrderId);
        Assert.Equal(1, closed.TotalCount);
        Assert.Equal(toReject.OrderId, closed.Items[0].OrderId);
        Assert.Equal(3, all.TotalCount);
        Assert.All(all.Items, o => Assert.Equal("Chanda Mwila", o.BuyerName));
    }

    [Fact]
    public async Task Detail_shows_the_buyer_phone_only_while_the_order_is_open()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var seller = await SeedSellerAsync(c);
        var placed = await PlaceAsync(c, seller);

        var open = await c.Orders.GetAsync(seller.UserId, placed.OrderId);
        Assert.NotNull(open);
        Assert.Equal("Chanda Mwila", open!.BuyerName);
        Assert.Equal("0971234567", open.BuyerPhone);
        Assert.Single(open.Lines);
        Assert.Equal(100m, open.Total);

        await c.Transitions.SellerRejectAsync(seller.UserId, placed.OrderId, "Sold out");
        var closed = await c.Orders.GetAsync(seller.UserId, placed.OrderId);
        Assert.NotNull(closed);
        Assert.Null(closed!.BuyerPhone);
        Assert.Equal("Chanda Mwila", closed.BuyerName);
    }

    [Fact]
    public async Task Detail_offers_only_the_actions_the_rules_allow()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var seller = await SeedSellerAsync(c);
        var placed = await PlaceAsync(c, seller);

        var pending = await c.Orders.GetAsync(seller.UserId, placed.OrderId);
        Assert.Equal(new[] { OrderStatus.Accepted, OrderStatus.Rejected }, pending!.Actions);

        await c.Transitions.SellerAcceptAsync(seller.UserId, placed.OrderId);
        var accepted = await c.Orders.GetAsync(seller.UserId, placed.OrderId);
        Assert.Equal(new[] { OrderStatus.ReadyForPickup }, accepted!.Actions);

        await c.Transitions.SellerMarkReadyAsync(seller.UserId, placed.OrderId);
        await c.Transitions.SellerCompleteAsync(seller.UserId, placed.OrderId);
        var done = await c.Orders.GetAsync(seller.UserId, placed.OrderId);
        Assert.Empty(done!.Actions);
        Assert.Equal(PaymentStatus.Paid, done.PaymentStatus);
    }

    [Fact]
    public async Task Another_sellers_order_is_not_returned_and_is_audited()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var owner = await SeedSellerAsync(c, "Owner");
        var stranger = await SeedSellerAsync(c, "Stranger");
        var placed = await PlaceAsync(c, owner);

        var detail = await c.Orders.GetAsync(stranger.UserId, placed.OrderId);

        Assert.Null(detail);
        Assert.True(await c.Db.AuditLogs.AnyAsync(a =>
            a.Action == "AccessDenied"
            && a.ActorUserId == stranger.UserId
            && a.EntityId == placed.OrderId.ToString()));
        Assert.Equal(0, (await c.Orders.ListAsync(stranger.UserId, SellerOrderFilter.All, 1)).TotalCount);
    }
}
