using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

public class WishlistServiceTests
{
    private static ZitbabaDbContext Db(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();

    private static SellerProfile AddSeller(
        ZitbabaDbContext db, string store,
        SellerApplicationStatus status = SellerApplicationStatus.Approved)
    {
        var seller = new SellerProfile { UserId = Guid.NewGuid(), StoreName = store, ApplicationStatus = status };
        db.SellerProfiles.Add(seller);
        return seller;
    }

    private static Product AddProduct(
        ZitbabaDbContext db, SellerProfile seller, Category category, string name,
        ProductStatus status = ProductStatus.Active)
    {
        var product = new Product
        {
            SellerProfile = seller,
            Category = category,
            Name = name,
            Price = 10m,
            QuantityAvailable = 5,
            Status = status
        };
        db.Products.Add(product);
        return product;
    }

    [Fact]
    public async Task Adding_a_visible_product_saves_it_once()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = new Category { Name = "Misc" };
        var product = AddProduct(db, AddSeller(db, "Store"), category, "Thing");
        await db.SaveChangesAsync();
        var service = new WishlistService(db);
        var userId = Guid.NewGuid();

        Assert.Equal(WishlistOutcome.Added, await service.AddAsync(userId, product.ProductId));
        Assert.Equal(WishlistOutcome.AlreadySaved, await service.AddAsync(userId, product.ProductId));

        Assert.Equal(1, await db.WishlistItems.CountAsync());
        Assert.True(await service.IsSavedAsync(userId, product.ProductId));
    }

    [Fact]
    public async Task Hidden_products_and_unknown_ids_cannot_be_saved()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = new Category { Name = "Misc" };
        var approved = AddSeller(db, "Approved Store");

        var hidden = new List<Product>();
        foreach (var status in Enum.GetValues<ProductStatus>().Where(s => s != ProductStatus.Active))
            hidden.Add(AddProduct(db, approved, category, "Item " + status, status));
        var suspended = AddSeller(db, "Suspended Store", SellerApplicationStatus.Suspended);
        hidden.Add(AddProduct(db, suspended, category, "Suspended seller item"));
        await db.SaveChangesAsync();
        var service = new WishlistService(db);
        var userId = Guid.NewGuid();

        foreach (var product in hidden)
            Assert.Equal(WishlistOutcome.NotAvailable, await service.AddAsync(userId, product.ProductId));
        Assert.Equal(WishlistOutcome.NotAvailable, await service.AddAsync(userId, Guid.NewGuid()));

        Assert.Equal(0, await db.WishlistItems.CountAsync());
    }

    [Fact]
    public async Task Wishlists_are_private_to_each_user()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = new Category { Name = "Misc" };
        var seller = AddSeller(db, "Store");
        var a = AddProduct(db, seller, category, "A");
        var b = AddProduct(db, seller, category, "B");
        await db.SaveChangesAsync();
        var service = new WishlistService(db);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();

        await service.AddAsync(alice, a.ProductId);
        await service.AddAsync(bob, b.ProductId);

        var aliceList = await service.ListAsync(alice, 1);
        var bobList = await service.ListAsync(bob, 1);
        Assert.Equal("A", Assert.Single(aliceList.Items).Name);
        Assert.Equal("B", Assert.Single(bobList.Items).Name);
        Assert.False(await service.IsSavedAsync(alice, b.ProductId));
    }

    [Fact]
    public async Task Remove_deletes_only_that_users_item_and_is_safe_when_not_saved()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = new Category { Name = "Misc" };
        var product = AddProduct(db, AddSeller(db, "Store"), category, "Thing");
        await db.SaveChangesAsync();
        var service = new WishlistService(db);
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        await service.AddAsync(alice, product.ProductId);
        await service.AddAsync(bob, product.ProductId);

        Assert.True(await service.RemoveAsync(alice, product.ProductId));
        Assert.False(await service.RemoveAsync(alice, product.ProductId));

        Assert.False(await service.IsSavedAsync(alice, product.ProductId));
        Assert.True(await service.IsSavedAsync(bob, product.ProductId));
    }

    [Fact]
    public async Task List_hides_items_that_are_no_longer_public_and_shows_them_again_later()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = new Category { Name = "Misc" };
        var seller = AddSeller(db, "Store");
        var keep = AddProduct(db, seller, category, "Keep");
        var toggle = AddProduct(db, seller, category, "Toggle");
        await db.SaveChangesAsync();
        var service = new WishlistService(db);
        var userId = Guid.NewGuid();
        await service.AddAsync(userId, keep.ProductId);
        await service.AddAsync(userId, toggle.ProductId);

        toggle.Status = ProductStatus.Inactive;
        await db.SaveChangesAsync();
        var hiddenList = await service.ListAsync(userId, 1);

        toggle.Status = ProductStatus.Active;
        await db.SaveChangesAsync();
        var backList = await service.ListAsync(userId, 1);

        Assert.Equal("Keep", Assert.Single(hiddenList.Items).Name);
        Assert.Equal(2, backList.TotalCount);
    }

    [Fact]
    public async Task List_is_newest_first_and_paged_12_per_page()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = new Category { Name = "Misc" };
        var seller = AddSeller(db, "Store");
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        for (var i = 0; i < 13; i++)
        {
            var product = AddProduct(db, seller, category, $"Item {i:00}");
            db.WishlistItems.Add(new WishlistItem
            {
                UserId = userId, Product = product, CreatedAt = now.AddMinutes(-i)
            });
        }
        await db.SaveChangesAsync();
        var service = new WishlistService(db);

        var page1 = await service.ListAsync(userId, 1);
        var page2 = await service.ListAsync(userId, 2);
        var tooFar = await service.ListAsync(userId, 99);

        Assert.Equal(13, page1.TotalCount);
        Assert.Equal(12, page1.Items.Count);
        Assert.Equal("Item 00", page1.Items[0].Name);
        Assert.Equal("Item 12", Assert.Single(page2.Items).Name);
        Assert.Equal(2, tooFar.Page);
    }
}
