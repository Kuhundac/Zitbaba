using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

public class CheckoutServiceTests
{
    private sealed record Ctx(ZitbabaDbContext Db, ICartService Cart, ICheckoutService Checkout);

    private static Ctx Resolve(IServiceScope scope) => new(
        scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>(),
        scope.ServiceProvider.GetRequiredService<ICartService>(),
        scope.ServiceProvider.GetRequiredService<ICheckoutService>());

    private static async Task<SellerProfile> SeedSellerAsync(ZitbabaDbContext db, string store = "Store A")
    {
        var seller = new SellerProfile
        {
            UserId = Guid.NewGuid(),
            StoreName = store,
            ApplicationStatus = SellerApplicationStatus.Approved
        };
        db.SellerProfiles.Add(seller);
        await db.SaveChangesAsync();
        return seller;
    }

    private static async Task<Product> SeedProductAsync(
        ZitbabaDbContext db, SellerProfile seller, decimal price = 100m, int qty = 5, string name = "Desk lamp")
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

    private static async Task UpdateProductAsync(ZitbabaDbContext db, Guid id, Action<Product> change)
    {
        var product = await db.Products.SingleAsync(p => p.ProductId == id);
        change(product);
        await db.SaveChangesAsync();
    }

    private static async Task<int> StockOfAsync(ZitbabaDbContext db, Guid id) =>
        (await db.Products.AsNoTracking().SingleAsync(p => p.ProductId == id)).QuantityAvailable;

    [Fact]
    public async Task Place_order_creates_order_snapshot_history_audit_and_clears_cart()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var seller = await SeedSellerAsync(c.Db);
        var lamp = await SeedProductAsync(c.Db, seller, 100m, 5, "Lamp");
        var kettle = await SeedProductAsync(c.Db, seller, 50m, 4, "Kettle");
        var buyer = Guid.NewGuid();
        await c.Cart.AddAsync(buyer, lamp.ProductId, 2);
        await c.Cart.AddAsync(buyer, kettle.ProductId, 1);

        var result = await c.Checkout.PlaceOrderAsync(buyer, Guid.NewGuid(), 250m);

        Assert.Equal(CheckoutOutcome.Placed, result.Outcome);
        Assert.Matches(@"^ZB-\d{8}-[A-HJ-NP-Z2-9]{6}$", result.OrderNumber!);

        var order = await c.Db.Orders.AsNoTracking()
            .Include(o => o.Items).Include(o => o.History).SingleAsync();
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);
        Assert.Equal(PaymentMethod.Cash, order.PaymentMethod);
        Assert.Equal(FulfilmentMethod.Pickup, order.FulfilmentMethod);
        Assert.Equal(250m, order.TotalAmount);
        Assert.Equal(buyer, order.BuyerUserId);
        Assert.Equal(seller.SellerProfileId, order.SellerProfileId);

        Assert.Equal(2, order.Items.Count);
        var lampLine = order.Items.Single(i => i.ProductName == "Lamp");
        Assert.Equal(100m, lampLine.UnitPrice);
        Assert.Equal(2, lampLine.Quantity);
        Assert.Equal(200m, lampLine.LineTotal);

        var history = Assert.Single(order.History);
        Assert.Null(history.FromStatus);
        Assert.Equal(OrderStatus.Pending, history.ToStatus);
        Assert.Equal(buyer, history.ActorUserId);

        Assert.True(await c.Db.AuditLogs.AnyAsync(a =>
            a.Action == "OrderPlaced" && a.EntityId == order.OrderId.ToString()));

        Assert.True((await c.Cart.GetAsync(buyer)).IsEmpty);
        Assert.Equal(3, await StockOfAsync(c.Db, lamp.ProductId));
        Assert.Equal(3, await StockOfAsync(c.Db, kettle.ProductId));
    }

    [Fact]
    public async Task Order_keeps_name_and_price_after_the_product_changes()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var seller = await SeedSellerAsync(c.Db);
        var lamp = await SeedProductAsync(c.Db, seller, 100m, 5, "Lamp");
        var buyer = Guid.NewGuid();
        await c.Cart.AddAsync(buyer, lamp.ProductId, 1);
        await c.Checkout.PlaceOrderAsync(buyer, Guid.NewGuid(), 100m);

        await UpdateProductAsync(c.Db, lamp.ProductId, p => { p.Price = 999m; p.Name = "Renamed"; });

        var item = await c.Db.OrderItems.AsNoTracking().SingleAsync();
        Assert.Equal("Lamp", item.ProductName);
        Assert.Equal(100m, item.UnitPrice);
    }

    [Fact]
    public async Task Empty_cart_is_refused()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);

        var result = await c.Checkout.PlaceOrderAsync(Guid.NewGuid(), Guid.NewGuid(), 0m);

        Assert.Equal(CheckoutOutcome.EmptyCart, result.Outcome);
        Assert.Empty(await c.Db.Orders.ToListAsync());
    }

    [Fact]
    public async Task Same_submission_key_twice_returns_the_same_order_and_takes_stock_once()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var seller = await SeedSellerAsync(c.Db);
        var lamp = await SeedProductAsync(c.Db, seller, 100m, 5);
        var buyer = Guid.NewGuid();
        var key = Guid.NewGuid();
        await c.Cart.AddAsync(buyer, lamp.ProductId, 2);

        var first = await c.Checkout.PlaceOrderAsync(buyer, key, 200m);
        var second = await c.Checkout.PlaceOrderAsync(buyer, key, 200m);

        Assert.Equal(CheckoutOutcome.Placed, first.Outcome);
        Assert.False(first.AlreadyPlaced);
        Assert.Equal(CheckoutOutcome.Placed, second.Outcome);
        Assert.True(second.AlreadyPlaced);
        Assert.Equal(first.OrderId, second.OrderId);
        Assert.Equal(first.OrderNumber, second.OrderNumber);
        Assert.Single(await c.Db.Orders.ToListAsync());
        Assert.Equal(3, await StockOfAsync(c.Db, lamp.ProductId));
    }

    [Fact]
    public async Task Short_stock_is_refused_and_nothing_is_created()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var seller = await SeedSellerAsync(c.Db);
        var lamp = await SeedProductAsync(c.Db, seller, 100m, 5, "Lamp");
        var buyer = Guid.NewGuid();
        await c.Cart.AddAsync(buyer, lamp.ProductId, 3);

        await UpdateProductAsync(c.Db, lamp.ProductId, p => p.QuantityAvailable = 1);
        var result = await c.Checkout.PlaceOrderAsync(buyer, Guid.NewGuid(), 300m);

        Assert.Equal(CheckoutOutcome.NotEnoughStock, result.Outcome);
        Assert.Contains("Only 1", result.Message);
        Assert.Empty(await c.Db.Orders.ToListAsync());
        Assert.Equal(1, await StockOfAsync(c.Db, lamp.ProductId));
        Assert.Equal(3, await c.Cart.CountAsync(buyer));
    }

    [Fact]
    public async Task Changed_price_is_refused_and_stock_is_untouched()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var seller = await SeedSellerAsync(c.Db);
        var lamp = await SeedProductAsync(c.Db, seller, 100m, 5);
        var buyer = Guid.NewGuid();
        await c.Cart.AddAsync(buyer, lamp.ProductId, 1);

        await UpdateProductAsync(c.Db, lamp.ProductId, p => p.Price = 120m);
        var result = await c.Checkout.PlaceOrderAsync(buyer, Guid.NewGuid(), 100m);

        Assert.Equal(CheckoutOutcome.PriceChanged, result.Outcome);
        Assert.Empty(await c.Db.Orders.ToListAsync());
        Assert.Equal(5, await StockOfAsync(c.Db, lamp.ProductId));
        Assert.Equal(1, await c.Cart.CountAsync(buyer));
    }

    [Fact]
    public async Task Deactivated_product_is_refused()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var seller = await SeedSellerAsync(c.Db);
        var lamp = await SeedProductAsync(c.Db, seller, 100m, 5);
        var buyer = Guid.NewGuid();
        await c.Cart.AddAsync(buyer, lamp.ProductId, 1);

        await UpdateProductAsync(c.Db, lamp.ProductId, p => p.Status = ProductStatus.Inactive);
        var result = await c.Checkout.PlaceOrderAsync(buyer, Guid.NewGuid(), 100m);

        Assert.Equal(CheckoutOutcome.NotEnoughStock, result.Outcome);
        Assert.Contains("no longer available", result.Message);
        Assert.Empty(await c.Db.Orders.ToListAsync());
    }

    [Fact]
    public async Task Two_orders_get_different_numbers_and_each_takes_stock()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var seller = await SeedSellerAsync(c.Db);
        var lamp = await SeedProductAsync(c.Db, seller, 100m, 5);
        var buyer = Guid.NewGuid();

        await c.Cart.AddAsync(buyer, lamp.ProductId, 1);
        var first = await c.Checkout.PlaceOrderAsync(buyer, Guid.NewGuid(), 100m);
        await c.Cart.AddAsync(buyer, lamp.ProductId, 1);
        var second = await c.Checkout.PlaceOrderAsync(buyer, Guid.NewGuid(), 100m);

        Assert.Equal(CheckoutOutcome.Placed, first.Outcome);
        Assert.Equal(CheckoutOutcome.Placed, second.Outcome);
        Assert.NotEqual(first.OrderNumber, second.OrderNumber);
        Assert.Equal(2, (await c.Db.Orders.ToListAsync()).Count);
        Assert.Equal(3, await StockOfAsync(c.Db, lamp.ProductId));
    }

    [Fact]
    public async Task Seller_who_is_no_longer_approved_is_refused()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var c = Resolve(scope);
        var seller = await SeedSellerAsync(c.Db);
        var lamp = await SeedProductAsync(c.Db, seller, 100m, 5);
        var buyer = Guid.NewGuid();
        await c.Cart.AddAsync(buyer, lamp.ProductId, 1);

        var tracked = await c.Db.SellerProfiles.SingleAsync(s => s.SellerProfileId == seller.SellerProfileId);
        tracked.ApplicationStatus = SellerApplicationStatus.Suspended;
        await c.Db.SaveChangesAsync();

        var result = await c.Checkout.PlaceOrderAsync(buyer, Guid.NewGuid(), 100m);

        Assert.Equal(CheckoutOutcome.NotAvailable, result.Outcome);
        Assert.Empty(await c.Db.Orders.ToListAsync());
        Assert.Equal(5, await StockOfAsync(c.Db, lamp.ProductId));
    }
}
