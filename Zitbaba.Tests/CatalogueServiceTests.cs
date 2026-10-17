using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

public class CatalogueServiceTests
{
    private static ZitbabaDbContext Db(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();

    private static CatalogueService Service(IServiceScope scope) => new(Db(scope));

    private static SellerProfile AddSeller(
        ZitbabaDbContext db, string store,
        SellerApplicationStatus status = SellerApplicationStatus.Approved)
    {
        var seller = new SellerProfile { UserId = Guid.NewGuid(), StoreName = store, ApplicationStatus = status };
        db.SellerProfiles.Add(seller);
        return seller;
    }

    private static Category AddCategory(
        ZitbabaDbContext db, string name, Category? parent = null, bool active = true)
    {
        var category = new Category { Name = name, ParentCategory = parent, IsActive = active };
        db.Categories.Add(category);
        return category;
    }

    private static Product AddProduct(
        ZitbabaDbContext db, SellerProfile seller, Category category, string name,
        decimal price = 10m, int qty = 5, ProductStatus status = ProductStatus.Active,
        string? description = null, DateTime? createdAt = null,
        ProductCondition condition = ProductCondition.New, int? lowStock = null)
    {
        var product = new Product
        {
            SellerProfile = seller,
            Category = category,
            Name = name,
            Description = description,
            Price = price,
            QuantityAvailable = qty,
            Status = status,
            Condition = condition,
            LowStockThreshold = lowStock,
            CreatedAt = createdAt ?? DateTime.UtcNow
        };
        db.Products.Add(product);
        return product;
    }

    private static string[] Names(PagedResult<CatalogueItem> result) =>
        result.Items.Select(i => i.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task Only_active_products_from_approved_sellers_are_public()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = AddCategory(db, "Books");
        var approved = AddSeller(db, "Approved Store");

        foreach (var status in Enum.GetValues<ProductStatus>())
            AddProduct(db, approved, category, "Item " + status, status: status);

        foreach (var sellerStatus in new[]
        {
            SellerApplicationStatus.Pending,
            SellerApplicationStatus.Rejected,
            SellerApplicationStatus.Suspended
        })
        {
            var seller = AddSeller(db, "Store " + sellerStatus, sellerStatus);
            AddProduct(db, seller, category, "Hidden " + sellerStatus);
        }
        await db.SaveChangesAsync();

        var result = await Service(scope).SearchAsync(new CatalogueQuery());

        var item = Assert.Single(result.Items);
        Assert.Equal("Item Active", item.Name);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task Keyword_search_ignores_case_and_requires_every_word()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = AddCategory(db, "Books");
        var seller = AddSeller(db, "Store");
        AddProduct(db, seller, category, "Calculus Textbook", description: "Used engineering maths");
        AddProduct(db, seller, category, "Scientific Calculator");
        AddProduct(db, seller, category, "Blue Notebook", description: "ruled pages for calculus notes");
        await db.SaveChangesAsync();
        var service = Service(scope);

        Assert.Equal(new[] { "Blue Notebook", "Calculus Textbook" },
            Names(await service.SearchAsync(new CatalogueQuery(Keyword: "calculus"))));
        Assert.Equal(new[] { "Calculus Textbook" },
            Names(await service.SearchAsync(new CatalogueQuery(Keyword: "CALCULUS textbook"))));
        Assert.Equal(new[] { "Scientific Calculator" },
            Names(await service.SearchAsync(new CatalogueQuery(Keyword: "calculator"))));
        Assert.Empty((await service.SearchAsync(new CatalogueQuery(Keyword: "100%"))).Items);
        Assert.Equal(3, (await service.SearchAsync(new CatalogueQuery(Keyword: "   "))).TotalCount);
    }

    [Fact]
    public async Task Category_filter_includes_subcategories()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var electronics = AddCategory(db, "Electronics");
        var phones = AddCategory(db, "Phones", electronics);
        var books = AddCategory(db, "Books");
        var seller = AddSeller(db, "Store");
        AddProduct(db, seller, phones, "Phone");
        AddProduct(db, seller, electronics, "Charger");
        AddProduct(db, seller, books, "Novel");
        await db.SaveChangesAsync();
        var service = Service(scope);

        Assert.Equal(new[] { "Charger", "Phone" },
            Names(await service.SearchAsync(new CatalogueQuery(CategoryId: electronics.CategoryId))));
        Assert.Equal(new[] { "Phone" },
            Names(await service.SearchAsync(new CatalogueQuery(CategoryId: phones.CategoryId))));
        Assert.Equal(new[] { "Novel" },
            Names(await service.SearchAsync(new CatalogueQuery(CategoryId: books.CategoryId))));
        Assert.Empty((await service.SearchAsync(new CatalogueQuery(CategoryId: 9999))).Items);
    }

    [Fact]
    public async Task Price_and_condition_filters_work_and_bad_input_is_ignored()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = AddCategory(db, "Misc");
        var seller = AddSeller(db, "Store");
        AddProduct(db, seller, category, "Cheap", price: 5m, condition: ProductCondition.Used);
        AddProduct(db, seller, category, "Mid", price: 20m, condition: ProductCondition.New);
        AddProduct(db, seller, category, "Dear", price: 50m, condition: ProductCondition.LikeNew);
        await db.SaveChangesAsync();
        var service = Service(scope);

        Assert.Equal(new[] { "Dear", "Mid" },
            Names(await service.SearchAsync(new CatalogueQuery(MinPrice: 10m))));
        Assert.Equal(new[] { "Cheap", "Mid" },
            Names(await service.SearchAsync(new CatalogueQuery(MaxPrice: 30m))));
        Assert.Equal(new[] { "Mid" },
            Names(await service.SearchAsync(new CatalogueQuery(MinPrice: 10m, MaxPrice: 30m))));
        // Min above max: the two values are swapped instead of returning nothing.
        Assert.Equal(new[] { "Mid" },
            Names(await service.SearchAsync(new CatalogueQuery(MinPrice: 30m, MaxPrice: 10m))));
        // A negative minimum is ignored.
        Assert.Equal(3, (await service.SearchAsync(new CatalogueQuery(MinPrice: -5m))).TotalCount);
        Assert.Equal(new[] { "Cheap" },
            Names(await service.SearchAsync(new CatalogueQuery(Condition: ProductCondition.Used))));
    }

    [Fact]
    public async Task Out_of_stock_items_are_listed_and_marked_and_stock_status_is_correct()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = AddCategory(db, "Misc");
        var seller = AddSeller(db, "Store");
        AddProduct(db, seller, category, "Gone", qty: 0);
        AddProduct(db, seller, category, "Few", qty: 2);
        AddProduct(db, seller, category, "Plenty", qty: 10);
        AddProduct(db, seller, category, "Custom", qty: 10, lowStock: 10);
        await db.SaveChangesAsync();
        var service = Service(scope);

        var all = await service.SearchAsync(new CatalogueQuery());
        var stock = all.Items.ToDictionary(i => i.Name, i => i.Stock);

        Assert.Equal(4, all.TotalCount);
        Assert.Equal(CatalogueStock.OutOfStock, stock["Gone"]);
        Assert.Equal(CatalogueStock.LowStock, stock["Few"]);
        Assert.Equal(CatalogueStock.InStock, stock["Plenty"]);
        Assert.Equal(CatalogueStock.LowStock, stock["Custom"]);

        Assert.Equal(new[] { "Custom", "Few", "Plenty" },
            Names(await service.SearchAsync(new CatalogueQuery(InStockOnly: true))));
    }

    [Fact]
    public async Task Sorting_by_newest_and_by_price_works()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = AddCategory(db, "Misc");
        var seller = AddSeller(db, "Store");
        var now = DateTime.UtcNow;
        AddProduct(db, seller, category, "A", price: 30m, createdAt: now.AddMinutes(-3));
        AddProduct(db, seller, category, "B", price: 10m, createdAt: now.AddMinutes(-2));
        AddProduct(db, seller, category, "C", price: 20m, createdAt: now.AddMinutes(-1));
        await db.SaveChangesAsync();
        var service = Service(scope);

        var newest = await service.SearchAsync(new CatalogueQuery(Sort: CatalogueSort.Newest));
        var low = await service.SearchAsync(new CatalogueQuery(Sort: CatalogueSort.PriceLowToHigh));
        var high = await service.SearchAsync(new CatalogueQuery(Sort: CatalogueSort.PriceHighToLow));

        Assert.Equal(new[] { "C", "B", "A" }, newest.Items.Select(i => i.Name).ToArray());
        Assert.Equal(new[] { "B", "C", "A" }, low.Items.Select(i => i.Name).ToArray());
        Assert.Equal(new[] { "A", "C", "B" }, high.Items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task Pagination_uses_12_per_page_and_clamps_the_page_number()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = AddCategory(db, "Misc");
        var seller = AddSeller(db, "Store");
        var now = DateTime.UtcNow;
        for (var i = 0; i < 25; i++)
            AddProduct(db, seller, category, $"Item {i:00}", createdAt: now.AddMinutes(-i));
        await db.SaveChangesAsync();
        var service = Service(scope);

        var page1 = await service.SearchAsync(new CatalogueQuery(Page: 1));
        var page2 = await service.SearchAsync(new CatalogueQuery(Page: 2));
        var page3 = await service.SearchAsync(new CatalogueQuery(Page: 3));
        var tooFar = await service.SearchAsync(new CatalogueQuery(Page: 99));
        var negative = await service.SearchAsync(new CatalogueQuery(Page: -5));

        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(12, page1.Items.Count);
        Assert.Equal(12, page2.Items.Count);
        Assert.Single(page3.Items);
        Assert.Equal(3, tooFar.Page);
        Assert.Single(tooFar.Items);
        Assert.Equal(1, negative.Page);

        var allIds = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(i => i.ProductId).Distinct().Count();
        Assert.Equal(25, allIds);
    }

    [Fact]
    public async Task Image_is_the_first_by_sort_order_and_prefers_the_thumbnail()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = AddCategory(db, "Misc");
        var seller = AddSeller(db, "Store");
        var now = DateTime.UtcNow;
        var pictured = AddProduct(db, seller, category, "Pictured", createdAt: now);
        AddProduct(db, seller, category, "Plain", createdAt: now.AddMinutes(-1));
        db.ProductImages.Add(new ProductImage
        {
            Product = pictured, ImageUrl = "/uploads/products/b.jpg", SortOrder = 1
        });
        db.ProductImages.Add(new ProductImage
        {
            Product = pictured, ImageUrl = "/uploads/products/a.jpg",
            ThumbnailUrl = "/uploads/products/a-thumb.jpg", SortOrder = 0
        });
        await db.SaveChangesAsync();

        var result = await Service(scope).SearchAsync(new CatalogueQuery());
        var urls = result.Items.ToDictionary(i => i.Name, i => i.ImageUrl);

        Assert.Equal("/uploads/products/a-thumb.jpg", urls["Pictured"]);
        Assert.Null(urls["Plain"]);
    }

    [Fact]
    public async Task Category_tree_lists_only_active_categories()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var a = AddCategory(db, "A");
        AddCategory(db, "A1", a);
        AddCategory(db, "A2", a, active: false);
        var b = AddCategory(db, "B", active: false);
        AddCategory(db, "B1", b);
        await db.SaveChangesAsync();

        var tree = await Service(scope).GetCategoriesAsync();

        var top = Assert.Single(tree);
        Assert.Equal("A", top.Name);
        var child = Assert.Single(top.Children);
        Assert.Equal("A1", child.Name);
    }

    [Fact]
    public async Task Recent_returns_newest_public_products_only()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var category = AddCategory(db, "Misc");
        var seller = AddSeller(db, "Store");
        var now = DateTime.UtcNow;
        for (var i = 0; i < 5; i++)
            AddProduct(db, seller, category, "P" + i, createdAt: now.AddMinutes(-i - 1));
        AddProduct(db, seller, category, "Draft", status: ProductStatus.Draft, createdAt: now);
        await db.SaveChangesAsync();

        var recent = await Service(scope).GetRecentAsync(3);

        Assert.Equal(new[] { "P0", "P1", "P2" }, recent.Select(i => i.Name).ToArray());
    }
}
