using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

public class CategoryServiceTests
{
    private static readonly Guid Admin = Guid.NewGuid();

    private static (ICategoryService Svc, ZitbabaDbContext Db) Resolve(IServiceScope scope) =>
        (scope.ServiceProvider.GetRequiredService<ICategoryService>(),
         scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>());

    [Fact]
    public async Task Create_trims_name_and_writes_audit_row()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (svc, db) = Resolve(scope);

        var result = await svc.CreateAsync(Admin, "  Gadgets ", null, 5);

        Assert.True(result.Succeeded);
        var category = await db.Categories.SingleAsync();
        Assert.Equal("Gadgets", category.Name);
        var audits = await db.AuditLogs.ToListAsync();
        Assert.Contains(audits, a => a.Action == "CategoryCreated" && a.EntityId == category.CategoryId.ToString());
    }

    [Fact]
    public async Task Duplicate_name_under_same_parent_is_rejected_ignoring_case()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (svc, _) = Resolve(scope);

        Assert.True((await svc.CreateAsync(Admin, "Fashion", null, 0)).Succeeded);
        var duplicate = await svc.CreateAsync(Admin, "fashion", null, 0);

        Assert.False(duplicate.Succeeded);
    }

    [Fact]
    public async Task Same_name_under_different_parents_is_allowed()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (svc, _) = Resolve(scope);

        var a = await svc.CreateAsync(Admin, "Men", null, 0);
        var b = await svc.CreateAsync(Admin, "Women", null, 1);
        var first = await svc.CreateAsync(Admin, "Shoes", a.CategoryId, 0);
        var second = await svc.CreateAsync(Admin, "Shoes", b.CategoryId, 0);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
    }

    [Fact]
    public async Task Subcategory_of_a_subcategory_is_rejected()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (svc, _) = Resolve(scope);

        var parent = await svc.CreateAsync(Admin, "Electronics", null, 0);
        var child = await svc.CreateAsync(Admin, "Phones", parent.CategoryId, 0);
        var grandchild = await svc.CreateAsync(Admin, "Cases", child.CategoryId, 0);

        Assert.False(grandchild.Succeeded);
    }

    [Fact]
    public async Task Category_with_children_cannot_become_a_subcategory()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (svc, _) = Resolve(scope);

        var parent = await svc.CreateAsync(Admin, "Electronics", null, 0);
        await svc.CreateAsync(Admin, "Phones", parent.CategoryId, 0);
        var other = await svc.CreateAsync(Admin, "Other", null, 1);

        var result = await svc.UpdateAsync(Admin, parent.CategoryId!.Value, "Electronics", other.CategoryId, 0);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Category_cannot_be_its_own_parent()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (svc, _) = Resolve(scope);

        var created = await svc.CreateAsync(Admin, "Books", null, 0);
        var result = await svc.UpdateAsync(Admin, created.CategoryId!.Value, "Books", created.CategoryId, 0);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Delete_is_blocked_when_category_has_products()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (svc, db) = Resolve(scope);

        var created = await svc.CreateAsync(Admin, "Snacks", null, 0);
        db.Products.Add(new Product
        {
            SellerProfileId = Guid.NewGuid(),
            CategoryId = created.CategoryId!.Value,
            Name = "Biscuits",
            Price = 10m,
            QuantityAvailable = 5
        });
        await db.SaveChangesAsync();

        var result = await svc.DeleteAsync(Admin, created.CategoryId.Value);

        Assert.False(result.Succeeded);
        Assert.Equal(1, await db.Categories.CountAsync());
    }

    [Fact]
    public async Task Delete_works_for_an_empty_category_and_is_audited()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (svc, db) = Resolve(scope);

        var created = await svc.CreateAsync(Admin, "Temp", null, 0);
        var result = await svc.DeleteAsync(Admin, created.CategoryId!.Value);

        Assert.True(result.Succeeded);
        Assert.Empty(await db.Categories.ToListAsync());
        Assert.Contains(await db.AuditLogs.ToListAsync(), a => a.Action == "CategoryDeleted");
    }

    [Fact]
    public async Task Deactivate_and_reactivate_toggle_the_flag()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (svc, db) = Resolve(scope);

        var created = await svc.CreateAsync(Admin, "Services", null, 0);
        var id = created.CategoryId!.Value;

        await svc.SetActiveAsync(Admin, id, false);
        Assert.False((await db.Categories.SingleAsync()).IsActive);

        await svc.SetActiveAsync(Admin, id, true);
        Assert.True((await db.Categories.SingleAsync()).IsActive);
    }
}
