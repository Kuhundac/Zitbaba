using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

public class BuyerOrderServiceTests
{
    private sealed record Ctx(
        ZitbabaDbContext Db, ICartService Cart, ICheckoutService Checkout, IBuyerOrderService Orders);

    private static Ctx Resolve(IServiceScope scope) => new(
        scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>(),
        scope.ServiceProvider.GetRequiredService<ICartService>(),
        scope.ServiceProvider.GetRequiredService<ICheckoutService>(),
        scope.ServiceProvider.GetRequiredService<IBuyerOrderService>());

    private static async Task<SellerProfile> SeedSellerAsync(ZitbabaDbContext db)
    {
        var seller = new SellerProfile
        {
            UserId = Guid.NewGuid(),
            StoreName = "Store A",
            ApplicationStatus = SellerApplicationStatus.Approved
        };
        db.SellerProfiles.Add(seller);
        await db.SaveChangesAsync();
        return seller;
    }

    private static async Task<Product> SeedProductAsync(
        ZitbabaDbContext db, SellerProfile seller, decimal price, int qty, string name)
    {
        var product = new Product
        {
            SellerProfileId = seller.SellerProfileId,
            CategoryId = 1,
            Name = name,
            Price = price,
            QuantityAvailable = qty,
            Status = ProductStatus.Active
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    private static async Task<CheckoutResult> PlaceOneAsync(Ctx c, Guid buyer, Product product, int qty)
    {
        await c.Cart.AddAsync(buyer, product.ProductId, qty);
        return await c.Checkout.PlaceOrderAsync(buyer, Guid.NewGuid(), product.Price * qty);
    }

    [Fact]
    public async Task List_returns_only_my_orders()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var seller = await SeedSellerAsync(c.Db);
        var lamp = await SeedProductAsync(c.Db, seller, 100m, 10, "Lamp");
        var buyerA = Guid.NewGuid();
        var buyerB = Guid.NewGuid();

        await PlaceOneAsync(c, buyerA, lamp, 1);
        await PlaceOneAsync(c, buyerA, lamp, 2);
        await PlaceOneAsync(c, buyerB, lamp, 1);

        var listA = await c.Orders.ListAsync(buyerA, 1);
        var listB = await c.Orders.ListAsync(buyerB, 1);

        Assert.Equal(2, listA.TotalCount);
        Assert.Equal(2, listA.Items.Count);
        Assert.All(listA.Items, o => Assert.Equal("Store A", o.StoreName));
        Assert.Equal(3, listA.Items.Sum(o => o.ItemCount));
        Assert.Equal(1, listB.TotalCount);
    }

    [Fact]
    public async Task Get_returns_lines_history_and_totals()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var seller = await SeedSellerAsync(c.Db);
        var lamp = await SeedProductAsync(c.Db, seller, 100m, 5, "Lamp");
        var kettle = await SeedProductAsync(c.Db, seller, 50m, 5, "Kettle");
        var buyer = Guid.NewGuid();
        await c.Cart.AddAsync(buyer, lamp.ProductId, 2);
        await c.Cart.AddAsync(buyer, kettle.ProductId, 1);
        var placed = await c.Checkout.PlaceOrderAsync(buyer, Guid.NewGuid(), 250m);

        var detail = await c.Orders.GetAsync(buyer, placed.OrderId!.Value);

        Assert.NotNull(detail);
        Assert.Equal(placed.OrderNumber, detail!.OrderNumber);
        Assert.Equal("Store A", detail.StoreName);
        Assert.Equal(OrderStatus.Pending, detail.Status);
        Assert.Equal(PaymentStatus.Pending, detail.PaymentStatus);
        Assert.Equal(250m, detail.Total);
        Assert.Equal(2, detail.Lines.Count);
        Assert.Contains(detail.Lines, l => l.ProductName == "Lamp" && l.Quantity == 2 && l.LineTotal == 200m);
        var history = Assert.Single(detail.History);
        Assert.Equal(OrderStatus.Pending, history.To);
    }

    [Fact]
    public async Task Another_buyers_order_is_not_returned_and_is_audited()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var seller = await SeedSellerAsync(c.Db);
        var lamp = await SeedProductAsync(c.Db, seller, 100m, 5, "Lamp");
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var placed = await PlaceOneAsync(c, owner, lamp, 1);

        var detail = await c.Orders.GetAsync(stranger, placed.OrderId!.Value);

        Assert.Null(detail);
        Assert.True(await c.Db.AuditLogs.AnyAsync(a =>
            a.Action == "AccessDenied"
            && a.ActorUserId == stranger
            && a.EntityId == placed.OrderId.Value.ToString()));
    }

    [Fact]
    public async Task Unknown_order_is_not_returned_and_not_audited()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);

        var detail = await c.Orders.GetAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Null(detail);
        Assert.False(await c.Db.AuditLogs.AnyAsync(a => a.Action == "AccessDenied"));
    }
}
