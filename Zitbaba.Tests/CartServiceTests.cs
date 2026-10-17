using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

public class CartServiceTests
{
    private static async Task<SellerProfile> SeedSellerAsync(
        ZitbabaDbContext db, string store = "Store A",
        SellerApplicationStatus status = SellerApplicationStatus.Approved)
    {
        var seller = new SellerProfile
        {
            UserId = Guid.NewGuid(),
            StoreName = store,
            ApplicationStatus = status
        };
        db.SellerProfiles.Add(seller);
        await db.SaveChangesAsync();
        return seller;
    }

    private static async Task<Product> SeedProductAsync(
        ZitbabaDbContext db, SellerProfile seller, decimal price = 100m, int qty = 5,
        ProductStatus status = ProductStatus.Active, string name = "Desk lamp")
    {
        var product = new Product
        {
            SellerProfileId = seller.SellerProfileId,
            CategoryId = 1,
            Name = name,
            Price = price,
            QuantityAvailable = qty,
            Status = status
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }

    [Fact]
    public async Task Add_creates_cart_and_binds_seller()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var cart = scope.ServiceProvider.GetRequiredService<ICartService>();
        var seller = await SeedSellerAsync(db);
        var product = await SeedProductAsync(db, seller);
        var buyer = Guid.NewGuid();

        var result = await cart.AddAsync(buyer, product.ProductId, 2);

        Assert.Equal(CartOutcome.Ok, result.Outcome);
        var view = await cart.GetAsync(buyer);
        Assert.Equal(seller.SellerProfileId, view.SellerProfileId);
        Assert.Equal("Store A", view.StoreName);
        Assert.Single(view.Lines);
        Assert.Equal(2, view.ItemCount);
        Assert.Equal(200m, view.Total);
    }

    [Fact]
    public async Task Add_same_product_twice_adds_quantities()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var cart = scope.ServiceProvider.GetRequiredService<ICartService>();
        var seller = await SeedSellerAsync(db);
        var product = await SeedProductAsync(db, seller, qty: 10);
        var buyer = Guid.NewGuid();

        await cart.AddAsync(buyer, product.ProductId, 2);
        var result = await cart.AddAsync(buyer, product.ProductId, 3);

        Assert.Equal(CartOutcome.Ok, result.Outcome);
        var view = await cart.GetAsync(buyer);
        Assert.Single(view.Lines);
        Assert.Equal(5, view.Lines[0].Quantity);
    }

    [Fact]
    public async Task Add_more_than_stock_is_refused_and_cart_unchanged()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var cart = scope.ServiceProvider.GetRequiredService<ICartService>();
        var seller = await SeedSellerAsync(db);
        var product = await SeedProductAsync(db, seller, qty: 3);
        var buyer = Guid.NewGuid();

        await cart.AddAsync(buyer, product.ProductId, 2);
        var result = await cart.AddAsync(buyer, product.ProductId, 2);

        Assert.Equal(CartOutcome.NotEnoughStock, result.Outcome);
        var view = await cart.GetAsync(buyer);
        Assert.Equal(2, view.Lines[0].Quantity);
    }

    [Fact]
    public async Task Add_out_of_stock_is_refused()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var cart = scope.ServiceProvider.GetRequiredService<ICartService>();
        var seller = await SeedSellerAsync(db);
        var product = await SeedProductAsync(db, seller, qty: 0);
        var buyer = Guid.NewGuid();

        var result = await cart.AddAsync(buyer, product.ProductId, 1);

        Assert.Equal(CartOutcome.NotEnoughStock, result.Outcome);
        Assert.True((await cart.GetAsync(buyer)).IsEmpty);
    }

    [Fact]
    public async Task Add_from_second_seller_is_refused_and_names_current_store()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var cart = scope.ServiceProvider.GetRequiredService<ICartService>();
        var sellerA = await SeedSellerAsync(db, "Store A");
        var sellerB = await SeedSellerAsync(db, "Store B");
        var productA = await SeedProductAsync(db, sellerA, name: "Lamp");
        var productB = await SeedProductAsync(db, sellerB, name: "Kettle");
        var buyer = Guid.NewGuid();

        await cart.AddAsync(buyer, productA.ProductId, 1);
        var result = await cart.AddAsync(buyer, productB.ProductId, 1);

        Assert.Equal(CartOutcome.DifferentSeller, result.Outcome);
        Assert.Equal("Store A", result.CurrentStoreName);
        Assert.Contains("Store A", result.Message);
        var view = await cart.GetAsync(buyer);
        Assert.Single(view.Lines);
        Assert.Equal("Lamp", view.Lines[0].Name);
    }

    [Fact]
    public async Task Add_own_product_is_refused()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var cart = scope.ServiceProvider.GetRequiredService<ICartService>();
        var seller = await SeedSellerAsync(db);
        var product = await SeedProductAsync(db, seller);

        var result = await cart.AddAsync(seller.UserId, product.ProductId, 1);

        Assert.Equal(CartOutcome.OwnProduct, result.Outcome);
        Assert.True((await cart.GetAsync(seller.UserId)).IsEmpty);
    }

    [Fact]
    public async Task Add_inactive_product_is_refused()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var cart = scope.ServiceProvider.GetRequiredService<ICartService>();
        var seller = await SeedSellerAsync(db);
        var product = await SeedProductAsync(db, seller, status: ProductStatus.Inactive);

        var result = await cart.AddAsync(Guid.NewGuid(), product.ProductId, 1);

        Assert.Equal(CartOutcome.NotAvailable, result.Outcome);
    }

    [Fact]
    public async Task Add_from_unapproved_seller_is_refused()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var cart = scope.ServiceProvider.GetRequiredService<ICartService>();
        var seller = await SeedSellerAsync(db, status: SellerApplicationStatus.Pending);
        var product = await SeedProductAsync(db, seller);

        var result = await cart.AddAsync(Guid.NewGuid(), product.ProductId, 1);

        Assert.Equal(CartOutcome.NotAvailable, result.Outcome);
    }

    [Fact]
    public async Task Add_zero_quantity_is_invalid()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var cart = scope.ServiceProvider.GetRequiredService<ICartService>();
        var seller = await SeedSellerAsync(db);
        var product = await SeedProductAsync(db, seller);

        var result = await cart.AddAsync(Guid.NewGuid(), product.ProductId, 0);

        Assert.Equal(CartOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task SetQuantity_updates_line()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var cart = scope.ServiceProvider.GetRequiredService<ICartService>();
        var seller = await SeedSellerAsync(db);
        var product = await SeedProductAsync(db, seller, qty: 10);
        var buyer = Guid.NewGuid();
        await cart.AddAsync(buyer, product.ProductId, 1);

        var result = await cart.SetQuantityAsync(buyer, product.ProductId, 4);

        Assert.Equal(CartOutcome.Ok, result.Outcome);
        Assert.Equal(4, (await cart.GetAsync(buyer)).Lines[0].Quantity);
        Assert.Equal(4, await cart.CountAsync(buyer));
    }

    [Fact]
    public async Task SetQuantity_above_stock_is_refused()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var cart = scope.ServiceProvider.GetRequiredService<ICartService>();
        var seller = await SeedSellerAsync(db);
        var product = await SeedProductAsync(db, seller, qty: 3);
        var buyer = Guid.NewGuid();
        await cart.AddAsync(buyer, product.ProductId, 1);

        var result = await cart.SetQuantityAsync(buyer, product.ProductId, 4);

        Assert.Equal(CartOutcome.NotEnoughStock, result.Outcome);
        Assert.Equal(1, (await cart.GetAsync(buyer)).Lines[0].Quantity);
    }

    [Fact]
    public async Task SetQuantity_zero_removes_line()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var cart = scope.ServiceProvider.GetRequiredService<ICartService>();
        var seller = await SeedSellerAsync(db);
        var product = await SeedProductAsync(db, seller);
        var buyer = Guid.NewGuid();
        await cart.AddAsync(buyer, product.ProductId, 2);

        var result = await cart.SetQuantityAsync(buyer, product.ProductId, 0);

        Assert.Equal(CartOutcome.Ok, result.Outcome);
        Assert.True((await cart.GetAsync(buyer)).IsEmpty);
    }

    [Fact]
    public async Task Removing_last_item_frees_cart_for_another_seller()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var cart = scope.ServiceProvider.GetRequiredService<ICartService>();
        var sellerA = await SeedSellerAsync(db, "Store A");
        var sellerB = await SeedSellerAsync(db, "Store B");
        var productA = await SeedProductAsync(db, sellerA);
        var productB = await SeedProductAsync(db, sellerB);
        var buyer = Guid.NewGuid();
        await cart.AddAsync(buyer, productA.ProductId, 1);

        await cart.RemoveAsync(buyer, productA.ProductId);
        var result = await cart.AddAsync(buyer, productB.ProductId, 1);

        Assert.Equal(CartOutcome.Ok, result.Outcome);
        Assert.Equal("Store B", (await cart.GetAsync(buyer)).StoreName);
    }

    [Fact]
    public async Task Clear_empties_cart_and_allows_other_seller()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var cart = scope.ServiceProvider.GetRequiredService<ICartService>();
        var sellerA = await SeedSellerAsync(db, "Store A");
        var sellerB = await SeedSellerAsync(db, "Store B");
        var productA = await SeedProductAsync(db, sellerA);
        var productB = await SeedProductAsync(db, sellerB);
        var buyer = Guid.NewGuid();
        await cart.AddAsync(buyer, productA.ProductId, 2);

        await cart.ClearAsync(buyer);

        Assert.True((await cart.GetAsync(buyer)).IsEmpty);
        Assert.Equal(0, await cart.CountAsync(buyer));
        var result = await cart.AddAsync(buyer, productB.ProductId, 1);
        Assert.Equal(CartOutcome.Ok, result.Outcome);
    }

    [Fact]
    public async Task Get_flags_deactivated_product_and_uses_current_price()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var cart = scope.ServiceProvider.GetRequiredService<ICartService>();
        var seller = await SeedSellerAsync(db);
        var product = await SeedProductAsync(db, seller, price: 100m, qty: 5);
        var buyer = Guid.NewGuid();
        await cart.AddAsync(buyer, product.ProductId, 2);

        product.Price = 150m;
        product.Status = ProductStatus.Inactive;
        await db.SaveChangesAsync();

        var view = await cart.GetAsync(buyer);
        Assert.True(view.HasProblems);
        Assert.NotNull(view.Lines[0].Problem);
        Assert.Equal(150m, view.Lines[0].UnitPrice);
        Assert.Equal(300m, view.Total);
    }

    [Fact]
    public async Task Carts_are_separate_per_user()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var cart = scope.ServiceProvider.GetRequiredService<ICartService>();
        var seller = await SeedSellerAsync(db);
        var product = await SeedProductAsync(db, seller, qty: 10);
        var buyer1 = Guid.NewGuid();
        var buyer2 = Guid.NewGuid();

        await cart.AddAsync(buyer1, product.ProductId, 3);
        await cart.AddAsync(buyer2, product.ProductId, 1);

        Assert.Equal(3, await cart.CountAsync(buyer1));
        Assert.Equal(1, await cart.CountAsync(buyer2));
    }

    [Fact]
    public async Task Get_for_user_with_no_cart_is_empty()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var cart = scope.ServiceProvider.GetRequiredService<ICartService>();

        var view = await cart.GetAsync(Guid.NewGuid());

        Assert.True(view.IsEmpty);
        Assert.Null(view.StoreName);
        Assert.Equal(0m, view.Total);
        Assert.Equal(0, await cart.CountAsync(Guid.NewGuid()));
    }
}
