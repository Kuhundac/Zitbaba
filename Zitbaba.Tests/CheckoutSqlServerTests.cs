using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

/// <summary>Real SQL Server tests for what the in-memory provider cannot check: atomic stock updates,
/// transaction rollback, unique indexes and check constraints.</summary>
public class CheckoutSqlServerTests
{
    private static async Task<CheckoutResult> PlaceAsync(Guid buyer, Guid key, decimal expectedTotal)
    {
        await using var db = await SqlServerTestDb.NewContextAsync();
        var checkout = new CheckoutService(db, new AuditService(db));
        return await checkout.PlaceOrderAsync(buyer, key, expectedTotal);
    }

    [SqlServerFact]
    public async Task Many_buyers_racing_for_limited_stock_never_oversell()
    {
        for (var round = 0; round < 3; round++)
        {
            const int buyers = 6;
            const int stock = 2;

            await using var seed = await SqlServerTestDb.NewContextAsync();
            var seller = await SqlServerTestDb.SeedSellerAsync(seed);
            var lamp = await SqlServerTestDb.SeedProductAsync(seed, seller, 100m, stock, "Lamp");

            var buyerIds = new List<Guid>();
            for (var i = 0; i < buyers; i++)
            {
                var id = await SqlServerTestDb.SeedUserAsync(seed);
                var add = await new CartService(seed).AddAsync(id, lamp.ProductId, 1);
                Assert.Equal(CartOutcome.Ok, add.Outcome);
                buyerIds.Add(id);
            }

            var results = await Task.WhenAll(buyerIds.Select(id => PlaceAsync(id, Guid.NewGuid(), 100m)));

            Assert.Equal(stock, results.Count(r => r.Outcome == CheckoutOutcome.Placed));
            Assert.Equal(buyers - stock, results.Count(r => r.Outcome == CheckoutOutcome.NotEnoughStock));

            await using var check = await SqlServerTestDb.NewContextAsync();
            var left = (await check.Products.AsNoTracking().SingleAsync(p => p.ProductId == lamp.ProductId)).QuantityAvailable;
            Assert.Equal(0, left);
            Assert.Equal(stock, await check.Orders.CountAsync(o => o.SellerProfileId == seller.SellerProfileId));
        }
    }

    [SqlServerFact]
    public async Task Failure_on_a_later_line_rolls_back_stock_taken_on_an_earlier_line()
    {
        await using var seed = await SqlServerTestDb.NewContextAsync();
        var seller = await SqlServerTestDb.SeedSellerAsync(seed);
        var a = await SqlServerTestDb.SeedProductAsync(seed, seller, 100m, 5, "Lamp");
        var b = await SqlServerTestDb.SeedProductAsync(seed, seller, 100m, 5, "Kettle");

        // The service takes stock in ProductId order. Make the LAST one short so the first has
        // already been decremented when the failure happens.
        var ordered = new[] { a, b }.OrderBy(p => p.ProductId).ToList();
        var first = ordered[0];
        var last = ordered[1];

        var buyer = await SqlServerTestDb.SeedUserAsync(seed);
        var cart = new CartService(seed);
        Assert.Equal(CartOutcome.Ok, (await cart.AddAsync(buyer, first.ProductId, 2)).Outcome);
        Assert.Equal(CartOutcome.Ok, (await cart.AddAsync(buyer, last.ProductId, 2)).Outcome);

        await seed.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Products SET QuantityAvailable = 1 WHERE ProductId = {last.ProductId}");

        var result = await PlaceAsync(buyer, Guid.NewGuid(), 400m);

        Assert.Equal(CheckoutOutcome.NotEnoughStock, result.Outcome);

        await using var check = await SqlServerTestDb.NewContextAsync();
        var firstStock = (await check.Products.AsNoTracking().SingleAsync(p => p.ProductId == first.ProductId)).QuantityAvailable;
        var lastStock = (await check.Products.AsNoTracking().SingleAsync(p => p.ProductId == last.ProductId)).QuantityAvailable;
        Assert.Equal(5, firstStock);   // rolled back, not left at 3
        Assert.Equal(1, lastStock);
        Assert.Equal(0, await check.Orders.CountAsync(o => o.BuyerUserId == buyer));
        Assert.Equal(4, await new CartService(check).CountAsync(buyer));
    }

    [SqlServerFact]
    public async Task Simultaneous_double_submit_creates_one_order_and_takes_stock_once()
    {
        for (var round = 0; round < 5; round++)
        {
            await using var seed = await SqlServerTestDb.NewContextAsync();
            var seller = await SqlServerTestDb.SeedSellerAsync(seed);
            var lamp = await SqlServerTestDb.SeedProductAsync(seed, seller, 100m, 5, "Lamp");
            var buyer = await SqlServerTestDb.SeedUserAsync(seed);
            Assert.Equal(CartOutcome.Ok, (await new CartService(seed).AddAsync(buyer, lamp.ProductId, 2)).Outcome);
            var key = Guid.NewGuid();

            var results = await Task.WhenAll(
                PlaceAsync(buyer, key, 200m),
                PlaceAsync(buyer, key, 200m));

            Assert.All(results, r => Assert.Equal(CheckoutOutcome.Placed, r.Outcome));
            Assert.Single(results.Select(r => r.OrderId).Distinct());

            await using var check = await SqlServerTestDb.NewContextAsync();
            Assert.Equal(1, await check.Orders.CountAsync(o => o.BuyerUserId == buyer));
            var left = (await check.Products.AsNoTracking().SingleAsync(p => p.ProductId == lamp.ProductId)).QuantityAvailable;
            Assert.Equal(3, left);
        }
    }

    [SqlServerFact]
    public async Task Database_rejects_a_second_order_with_the_same_buyer_and_submission_key()
    {
        await using var seed = await SqlServerTestDb.NewContextAsync();
        var seller = await SqlServerTestDb.SeedSellerAsync(seed);
        var lamp = await SqlServerTestDb.SeedProductAsync(seed, seller, 100m, 5, "Lamp");
        var buyer = await SqlServerTestDb.SeedUserAsync(seed);
        await new CartService(seed).AddAsync(buyer, lamp.ProductId, 1);
        var key = Guid.NewGuid();
        var placed = await PlaceAsync(buyer, key, 100m);
        Assert.Equal(CheckoutOutcome.Placed, placed.Outcome);

        await using var db = await SqlServerTestDb.NewContextAsync();
        db.Orders.Add(new Order
        {
            OrderNumber = "ZB-TEST-" + Guid.NewGuid().ToString("N")[..10],
            BuyerUserId = buyer,
            SellerProfileId = seller.SellerProfileId,
            SubmissionKey = key,
            TotalAmount = 0m
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task Database_rejects_a_cart_item_with_zero_quantity()
    {
        await using var db = await SqlServerTestDb.NewContextAsync();
        var seller = await SqlServerTestDb.SeedSellerAsync(db);
        var lamp = await SqlServerTestDb.SeedProductAsync(db, seller, 100m, 5, "Lamp");
        var buyer = await SqlServerTestDb.SeedUserAsync(db);
        await new CartService(db).AddAsync(buyer, lamp.ProductId, 1);

        var item = await db.CartItems.SingleAsync(i => i.Cart!.UserId == buyer);
        item.Quantity = 0;

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [SqlServerFact]
    public async Task Database_rejects_an_order_with_a_negative_total()
    {
        await using var db = await SqlServerTestDb.NewContextAsync();
        var seller = await SqlServerTestDb.SeedSellerAsync(db);
        var buyer = await SqlServerTestDb.SeedUserAsync(db);

        db.Orders.Add(new Order
        {
            OrderNumber = "ZB-TEST-" + Guid.NewGuid().ToString("N")[..10],
            BuyerUserId = buyer,
            SellerProfileId = seller.SellerProfileId,
            SubmissionKey = Guid.NewGuid(),
            TotalAmount = -1m
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
