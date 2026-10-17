using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

public class ProductDetailServiceTests
{
    private static ZitbabaDbContext Db(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();

    private static ProductDetailService Service(IServiceScope scope) => new(Db(scope));

    private static SellerProfile AddSeller(
        ZitbabaDbContext db, string store,
        SellerApplicationStatus status = SellerApplicationStatus.Approved,
        decimal rating = 0m, int ratingCount = 0)
    {
        var seller = new SellerProfile
        {
            UserId = Guid.NewGuid(),
            StoreName = store,
            ApplicationStatus = status,
            RatingAverage = rating,
            RatingCount = ratingCount
        };
        db.SellerProfiles.Add(seller);
        return seller;
    }

    private static Product AddProduct(
        ZitbabaDbContext db, SellerProfile seller, Category category, string name,
        ProductStatus status = ProductStatus.Active, int qty = 5, int? lowStock = null)
    {
        var product = new Product
        {
            SellerProfile = seller,
            Category = category,
            Name = name,
            Description = "Details for " + name,
            Price = 25m,
            QuantityAvailable = qty,
            LowStockThreshold = lowStock,
            Status = status
        };
        db.Products.Add(product);
        return product;
    }

    [Fact]
    public async Task Visible_product_returns_public_details_with_images_in_order()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var parent = new Category { Name = "Electronics" };
        var child = new Category { Name = "Phones", ParentCategory = parent };
        db.Categories.AddRange(parent, child);
        var seller = AddSeller(db, "Kc Store", rating: 4.5m, ratingCount: 2);
        var product = AddProduct(db, seller, child, "Phone", qty: 10);
        db.ProductImages.Add(new ProductImage { Product = product, ImageUrl = "/uploads/products/b.jpg", SortOrder = 1 });
        db.ProductImages.Add(new ProductImage { Product = product, ImageUrl = "/uploads/products/a.jpg", SortOrder = 0 });
        await db.SaveChangesAsync();

        var detail = await Service(scope).GetAsync(product.ProductId);

        Assert.NotNull(detail);
        Assert.Equal("Phone", detail!.Name);
        Assert.Equal(25m, detail.Price);
        Assert.Equal("Phones", detail.CategoryName);
        Assert.Equal("Electronics", detail.ParentCategoryName);
        Assert.Equal(CatalogueStock.InStock, detail.Stock);
        Assert.Equal(new[] { "/uploads/products/a.jpg", "/uploads/products/b.jpg" }, detail.ImageUrls.ToArray());
        Assert.Equal("Kc Store", detail.Seller.StoreName);
        Assert.Equal(4.5m, detail.Seller.RatingAverage);
        Assert.Equal(2, detail.Seller.RatingCount);
    }

    [Fact]
    public async Task Hidden_products_and_unknown_ids_return_null()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = new Category { Name = "Misc" };
        db.Categories.Add(category);
        var approved = AddSeller(db, "Approved Store");

        var hidden = new List<Product>();
        foreach (var status in Enum.GetValues<ProductStatus>().Where(s => s != ProductStatus.Active))
            hidden.Add(AddProduct(db, approved, category, "Item " + status, status));

        foreach (var sellerStatus in new[]
        {
            SellerApplicationStatus.Pending,
            SellerApplicationStatus.Rejected,
            SellerApplicationStatus.Suspended
        })
        {
            var seller = AddSeller(db, "Store " + sellerStatus, sellerStatus);
            hidden.Add(AddProduct(db, seller, category, "Hidden " + sellerStatus));
        }
        await db.SaveChangesAsync();
        var service = Service(scope);

        foreach (var product in hidden)
            Assert.Null(await service.GetAsync(product.ProductId));

        Assert.Null(await service.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Out_of_stock_product_is_still_returned_and_marked()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = new Category { Name = "Misc" };
        db.Categories.Add(category);
        var seller = AddSeller(db, "Store");
        var product = AddProduct(db, seller, category, "Gone", qty: 0);
        await db.SaveChangesAsync();

        var detail = await Service(scope).GetAsync(product.ProductId);

        Assert.NotNull(detail);
        Assert.Equal(CatalogueStock.OutOfStock, detail!.Stock);
        Assert.Equal(0, detail.QuantityAvailable);
        Assert.Null(detail.ParentCategoryName);
        Assert.Empty(detail.ImageUrls);
    }

    [Fact]
    public async Task Low_stock_uses_the_default_or_custom_threshold()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = new Category { Name = "Misc" };
        db.Categories.Add(category);
        var seller = AddSeller(db, "Store");
        var few = AddProduct(db, seller, category, "Few", qty: 2);
        var plenty = AddProduct(db, seller, category, "Plenty", qty: 10);
        var custom = AddProduct(db, seller, category, "Custom", qty: 10, lowStock: 10);
        await db.SaveChangesAsync();
        var service = Service(scope);

        Assert.Equal(CatalogueStock.LowStock, (await service.GetAsync(few.ProductId))!.Stock);
        Assert.Equal(CatalogueStock.InStock, (await service.GetAsync(plenty.ProductId))!.Stock);
        Assert.Equal(CatalogueStock.LowStock, (await service.GetAsync(custom.ProductId))!.Stock);
    }

    [Fact]
    public async Task Seller_rating_is_hidden_until_there_are_ratings()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = new Category { Name = "Misc" };
        db.Categories.Add(category);
        var seller = AddSeller(db, "New Store", rating: 0m, ratingCount: 0);
        var product = AddProduct(db, seller, category, "Thing");
        await db.SaveChangesAsync();

        var detail = await Service(scope).GetAsync(product.ProductId);

        Assert.Null(detail!.Seller.RatingAverage);
        Assert.Equal(0, detail.Seller.RatingCount);
    }
}
