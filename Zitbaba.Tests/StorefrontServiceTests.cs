using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

public class StorefrontServiceTests
{
    private static ZitbabaDbContext Db(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();

    private static StorefrontService Service(IServiceScope scope)
    {
        var db = Db(scope);
        return new StorefrontService(db, new CatalogueService(db));
    }

    private static SellerProfile AddSeller(
        ZitbabaDbContext db, string store,
        SellerApplicationStatus status = SellerApplicationStatus.Approved,
        string? description = null, decimal rating = 0m, int ratingCount = 0)
    {
        var seller = new SellerProfile
        {
            UserId = Guid.NewGuid(),
            StoreName = store,
            Description = description,
            ApplicationStatus = status,
            RatingAverage = rating,
            RatingCount = ratingCount
        };
        db.SellerProfiles.Add(seller);
        return seller;
    }

    private static Product AddProduct(
        ZitbabaDbContext db, SellerProfile seller, Category category, string name,
        ProductStatus status = ProductStatus.Active, DateTime? createdAt = null)
    {
        var product = new Product
        {
            SellerProfile = seller,
            Category = category,
            Name = name,
            Price = 10m,
            QuantityAvailable = 5,
            Status = status,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
        db.Products.Add(product);
        return product;
    }

    [Fact]
    public async Task Returns_seller_info_and_only_that_sellers_active_products()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = new Category { Name = "Misc" };
        db.Categories.Add(category);
        var mine = AddSeller(db, "My Store", description: "Snacks and drinks", rating: 4m, ratingCount: 3);
        var other = AddSeller(db, "Other Store");
        AddProduct(db, mine, category, "Mine A");
        AddProduct(db, mine, category, "Mine B");
        AddProduct(db, other, category, "Theirs");
        await db.SaveChangesAsync();

        var store = await Service(scope).GetAsync(mine.SellerProfileId, 1);

        Assert.NotNull(store);
        Assert.Equal("My Store", store!.Seller.StoreName);
        Assert.Equal("Snacks and drinks", store.Description);
        Assert.Equal(4m, store.Seller.RatingAverage);
        Assert.Equal(3, store.Seller.RatingCount);
        Assert.Equal(2, store.Products.TotalCount);
        Assert.Equal(new[] { "Mine A", "Mine B" },
            store.Products.Items.Select(i => i.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Sellers_who_are_not_approved_and_unknown_ids_return_null()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = new Category { Name = "Misc" };
        db.Categories.Add(category);

        var sellers = new List<SellerProfile>();
        foreach (var status in new[]
        {
            SellerApplicationStatus.Pending,
            SellerApplicationStatus.Rejected,
            SellerApplicationStatus.Suspended
        })
        {
            var seller = AddSeller(db, "Store " + status, status);
            AddProduct(db, seller, category, "Item " + status);
            sellers.Add(seller);
        }
        await db.SaveChangesAsync();
        var service = Service(scope);

        foreach (var seller in sellers)
            Assert.Null(await service.GetAsync(seller.SellerProfileId, 1));

        Assert.Null(await service.GetAsync(Guid.NewGuid(), 1));
    }

    [Fact]
    public async Task Products_that_are_not_active_are_not_listed()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = new Category { Name = "Misc" };
        db.Categories.Add(category);
        var seller = AddSeller(db, "Store");
        foreach (var status in Enum.GetValues<ProductStatus>())
            AddProduct(db, seller, category, "Item " + status, status);
        await db.SaveChangesAsync();

        var store = await Service(scope).GetAsync(seller.SellerProfileId, 1);

        var item = Assert.Single(store!.Products.Items);
        Assert.Equal("Item Active", item.Name);
    }

    [Fact]
    public async Task Products_are_paged_12_per_page_and_the_page_is_clamped()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = new Category { Name = "Misc" };
        db.Categories.Add(category);
        var seller = AddSeller(db, "Store");
        var now = DateTime.UtcNow;
        for (var i = 0; i < 13; i++)
            AddProduct(db, seller, category, $"Item {i:00}", createdAt: now.AddMinutes(-i));
        await db.SaveChangesAsync();
        var service = Service(scope);

        var page1 = await service.GetAsync(seller.SellerProfileId, 1);
        var page2 = await service.GetAsync(seller.SellerProfileId, 2);
        var tooFar = await service.GetAsync(seller.SellerProfileId, 99);

        Assert.Equal(13, page1!.Products.TotalCount);
        Assert.Equal(12, page1.Products.Items.Count);
        Assert.Single(page2!.Products.Items);
        Assert.Equal(2, tooFar!.Products.Page);
    }

    [Fact]
    public async Task Rating_is_hidden_until_there_are_ratings()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var seller = AddSeller(db, "New Store", rating: 0m, ratingCount: 0);
        await db.SaveChangesAsync();

        var store = await Service(scope).GetAsync(seller.SellerProfileId, 1);

        Assert.Null(store!.Seller.RatingAverage);
        Assert.Equal(0, store.Products.TotalCount);
    }
}
