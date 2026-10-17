using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Models;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

public class SellerProductRulesTests
{
    private sealed record Seller(Guid UserId, Guid ProfileId);

    private static async Task<Seller> AddSellerAsync(IServiceScope scope, SellerApplicationStatus status)
    {
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var userId = Guid.NewGuid();
        var profile = new SellerProfile
        {
            UserId = userId,
            StoreName = "Store " + userId,
            ApplicationStatus = status
        };
        db.SellerProfiles.Add(profile);
        await db.SaveChangesAsync();
        return new Seller(userId, profile.SellerProfileId);
    }

    private static async Task<int> AddCategoryAsync(IServiceScope scope)
    {
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var category = new Category { Name = "Test category", IsActive = true };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.CategoryId;
    }

    private static async Task<Guid> AddProductAsync(IServiceScope scope, Guid sellerProfileId, int categoryId)
    {
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var product = new Product
        {
            SellerProfileId = sellerProfileId,
            CategoryId = categoryId,
            Name = "Original name",
            Price = 10m,
            QuantityAvailable = 5,
            Status = ProductStatus.Active
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product.ProductId;
    }

    private static SellerProductInput ValidInput(int categoryId) =>
        new("Test product", categoryId, "A description", 10m, 5, ProductCondition.New, null);

    // (a) Only an Approved seller can create products.
    [Theory]
    [InlineData(SellerApplicationStatus.Pending)]
    [InlineData(SellerApplicationStatus.Suspended)]
    public async Task Pending_or_suspended_seller_cannot_create_a_product(SellerApplicationStatus status)
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var seller = await AddSellerAsync(scope, status);
        var categoryId = await AddCategoryAsync(scope);
        var service = scope.ServiceProvider.GetRequiredService<ISellerProductService>();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();

        var result = await service.CreateAsync(seller.UserId, ValidInput(categoryId));

        Assert.Equal(ProductOutcome.NotAllowed, result.Outcome);
        Assert.Empty(await db.Products.ToListAsync());
    }

    [Fact]
    public async Task Approved_seller_can_create_a_product()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var seller = await AddSellerAsync(scope, SellerApplicationStatus.Approved);
        var categoryId = await AddCategoryAsync(scope);
        var service = scope.ServiceProvider.GetRequiredService<ISellerProductService>();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();

        var result = await service.CreateAsync(seller.UserId, ValidInput(categoryId));

        Assert.Equal(ProductOutcome.Ok, result.Outcome);
        var product = await db.Products.AsNoTracking().SingleAsync();
        Assert.Equal(seller.ProfileId, product.SellerProfileId);
        Assert.Equal(ProductStatus.Active, product.Status);
    }

    // (b) Ownership: seller A cannot touch seller B's product.
    [Fact]
    public async Task Seller_cannot_update_or_deactivate_another_sellers_product()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var sellerA = await AddSellerAsync(scope, SellerApplicationStatus.Approved);
        var sellerB = await AddSellerAsync(scope, SellerApplicationStatus.Approved);
        var categoryId = await AddCategoryAsync(scope);
        var productOfB = await AddProductAsync(scope, sellerB.ProfileId, categoryId);
        var service = scope.ServiceProvider.GetRequiredService<ISellerProductService>();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();

        var update = await service.UpdateAsync(
            sellerA.UserId, productOfB,
            new SellerProductInput("Hijacked", categoryId, null, 1m, 1, ProductCondition.New, null),
            rowVersion: null);
        var deactivate = await service.SetStatusAsync(sellerA.UserId, productOfB, ProductAction.Deactivate);

        Assert.Equal(ProductOutcome.NotFound, update.Outcome);
        Assert.Equal(ProductOutcome.NotFound, deactivate.Outcome);

        // The product is untouched.
        var product = await db.Products.AsNoTracking().SingleAsync(p => p.ProductId == productOfB);
        Assert.Equal("Original name", product.Name);
        Assert.Equal(ProductStatus.Active, product.Status);

        // The attempt was audited.
        Assert.NotEmpty(await db.AuditLogs.Where(a => a.Action == "AccessDenied").ToListAsync());
    }

    // (c) Invalid money and stock values are rejected.
    [Theory]
    [InlineData(-1, 5)]       // negative price
    [InlineData(10, -1)]      // negative quantity
    [InlineData(10.123, 5)]   // more than two decimals
    public async Task Invalid_price_or_quantity_is_rejected_on_create(decimal price, int quantity)
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var seller = await AddSellerAsync(scope, SellerApplicationStatus.Approved);
        var categoryId = await AddCategoryAsync(scope);
        var service = scope.ServiceProvider.GetRequiredService<ISellerProductService>();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();

        var result = await service.CreateAsync(seller.UserId,
            new SellerProductInput("Bad values", categoryId, null, price, quantity, ProductCondition.New, null));

        Assert.Equal(ProductOutcome.Invalid, result.Outcome);
        Assert.Empty(await db.Products.ToListAsync());
    }

    [Fact]
    public async Task Invalid_price_or_quantity_is_rejected_on_update()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var seller = await AddSellerAsync(scope, SellerApplicationStatus.Approved);
        var categoryId = await AddCategoryAsync(scope);
        var productId = await AddProductAsync(scope, seller.ProfileId, categoryId);
        var service = scope.ServiceProvider.GetRequiredService<ISellerProductService>();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();

        var badPrice = await service.UpdateAsync(seller.UserId, productId,
            new SellerProductInput("Original name", categoryId, null, -5m, 5, ProductCondition.New, null), null);
        var badQuantity = await service.UpdateAsync(seller.UserId, productId,
            new SellerProductInput("Original name", categoryId, null, 10m, -2, ProductCondition.New, null), null);

        Assert.Equal(ProductOutcome.Invalid, badPrice.Outcome);
        Assert.Equal(ProductOutcome.Invalid, badQuantity.Outcome);

        var product = await db.Products.AsNoTracking().SingleAsync();
        Assert.Equal(10m, product.Price);
        Assert.Equal(5, product.QuantityAvailable);
    }

    // (d) Listing moderation setting.
    [Fact]
    public async Task Moderation_setting_true_creates_products_as_PendingModeration()
    {
        using var host = new TestHost(new Dictionary<string, string?>
        {
            ["Marketplace:RequireListingModeration"] = "true"
        });
        using var scope = host.CreateScope();
        var seller = await AddSellerAsync(scope, SellerApplicationStatus.Approved);
        var categoryId = await AddCategoryAsync(scope);
        var service = scope.ServiceProvider.GetRequiredService<ISellerProductService>();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();

        var result = await service.CreateAsync(seller.UserId, ValidInput(categoryId));

        Assert.Equal(ProductOutcome.Ok, result.Outcome);
        Assert.Equal(ProductStatus.PendingModeration, (await db.Products.AsNoTracking().SingleAsync()).Status);
    }
}
