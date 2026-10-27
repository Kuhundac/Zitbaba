using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Infrastructure.Security;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

public class AdminUserServiceTests
{
    private static async Task<ApplicationUser> CreateUserAsync(
        UserManager<ApplicationUser> users, string email, string phone, string role, string name = "Test User")
    {
        var user = new ApplicationUser { UserName = email, Email = email, FullName = name, Phone = phone };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    private static AdminUserService Service(IServiceProvider sp) => new(
        sp.GetRequiredService<ZitbabaDbContext>(),
        sp.GetRequiredService<UserManager<ApplicationUser>>(),
        sp.GetRequiredService<IAuditService>());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no")]
    public async Task Suspend_requires_a_reason_of_at_least_5_characters(string? reason)
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var sp = scope.ServiceProvider;
        await TestHost.EnsureRolesAsync(sp);
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var db = sp.GetRequiredService<ZitbabaDbContext>();
        var admin = await CreateUserAsync(users, "admin@test.local", "0970000001", RoleNames.Admin);
        var target = await CreateUserAsync(users, "buyer@test.local", "0970000002", RoleNames.Buyer);

        var outcome = await Service(sp).SuspendAsync(target.Id, admin.Id, reason);

        Assert.Equal(AccountActionOutcome.ReasonRequired, outcome);
        var saved = await db.Users.AsNoTracking().SingleAsync(u => u.Id == target.Id);
        Assert.Equal(AccountStatus.Active, saved.AccountStatus);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Suspend_sets_status_rotates_the_security_stamp_and_writes_an_audit_row()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var sp = scope.ServiceProvider;
        await TestHost.EnsureRolesAsync(sp);
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var db = sp.GetRequiredService<ZitbabaDbContext>();
        var admin = await CreateUserAsync(users, "admin@test.local", "0970000001", RoleNames.Admin);
        var target = await CreateUserAsync(users, "buyer@test.local", "0970000002", RoleNames.Buyer);
        var stampBefore = target.SecurityStamp;

        var outcome = await Service(sp).SuspendAsync(target.Id, admin.Id, "Repeated abuse reports.");

        Assert.Equal(AccountActionOutcome.Done, outcome);
        var saved = await db.Users.AsNoTracking().SingleAsync(u => u.Id == target.Id);
        Assert.Equal(AccountStatus.Suspended, saved.AccountStatus);
        Assert.NotEqual(stampBefore, saved.SecurityStamp);
        var audit = Assert.Single(await db.AuditLogs.ToListAsync());
        Assert.Equal("AccountSuspended", audit.Action);
        Assert.Equal(admin.Id, audit.ActorUserId);
        Assert.Equal(target.Id.ToString(), audit.EntityId);
    }

    [Fact]
    public async Task You_cannot_suspend_yourself_or_an_administrator()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var sp = scope.ServiceProvider;
        await TestHost.EnsureRolesAsync(sp);
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var db = sp.GetRequiredService<ZitbabaDbContext>();
        var admin = await CreateUserAsync(users, "admin@test.local", "0970000001", RoleNames.Admin);
        var otherAdmin = await CreateUserAsync(users, "admin2@test.local", "0970000002", RoleNames.Admin);
        var service = Service(sp);

        Assert.Equal(AccountActionOutcome.CannotActOnSelf,
            await service.SuspendAsync(admin.Id, admin.Id, "A valid reason."));
        Assert.Equal(AccountActionOutcome.CannotSuspendAdmin,
            await service.SuspendAsync(otherAdmin.Id, admin.Id, "A valid reason."));

        var saved = await db.Users.AsNoTracking().Where(u => u.Id == admin.Id || u.Id == otherAdmin.Id).ToListAsync();
        Assert.All(saved, u => Assert.Equal(AccountStatus.Active, u.AccountStatus));
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Suspending_an_approved_seller_account_suspends_the_profile_and_hides_the_products()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var sp = scope.ServiceProvider;
        await TestHost.EnsureRolesAsync(sp);
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var db = sp.GetRequiredService<ZitbabaDbContext>();
        var admin = await CreateUserAsync(users, "admin@test.local", "0970000001", RoleNames.Admin);
        var target = await CreateUserAsync(users, "seller@test.local", "0970000002", RoleNames.Seller);

        var seller = new SellerProfile
        {
            UserId = target.Id, StoreName = "Test Store", ApplicationStatus = SellerApplicationStatus.Approved
        };
        var category = new Category { Name = "Misc" };
        db.SellerProfiles.Add(seller);
        db.Categories.Add(category);
        db.Products.Add(new Product
        {
            SellerProfile = seller, Category = category, Name = "Lamp", Price = 10m,
            QuantityAvailable = 5, Status = ProductStatus.Active
        });
        await db.SaveChangesAsync();
        var catalogue = new CatalogueService(db);
        Assert.Equal(1, (await catalogue.SearchAsync(new CatalogueQuery())).TotalCount);

        var outcome = await Service(sp).SuspendAsync(target.Id, admin.Id, "Selling banned items.");

        Assert.Equal(AccountActionOutcome.Done, outcome);
        Assert.Equal(0, (await catalogue.SearchAsync(new CatalogueQuery())).TotalCount);
        var profile = await db.SellerProfiles.AsNoTracking().SingleAsync();
        Assert.Equal(SellerApplicationStatus.Suspended, profile.ApplicationStatus);
        Assert.Equal(admin.Id, profile.ReviewedByUserId);
        var actions = (await db.AuditLogs.OrderBy(a => a.AuditLogId).ToListAsync()).Select(a => a.Action).ToArray();
        Assert.Equal(new[] { "AccountSuspended", "SellerSuspended" }, actions);
    }

    [Fact]
    public async Task Reactivate_restores_the_account_but_not_the_seller_profile()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var sp = scope.ServiceProvider;
        await TestHost.EnsureRolesAsync(sp);
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var db = sp.GetRequiredService<ZitbabaDbContext>();
        var admin = await CreateUserAsync(users, "admin@test.local", "0970000001", RoleNames.Admin);
        var target = await CreateUserAsync(users, "seller@test.local", "0970000002", RoleNames.Seller);
        db.SellerProfiles.Add(new SellerProfile
        {
            UserId = target.Id, StoreName = "Test Store", ApplicationStatus = SellerApplicationStatus.Approved
        });
        await db.SaveChangesAsync();
        var service = Service(sp);

        Assert.Equal(AccountActionOutcome.Done, await service.SuspendAsync(target.Id, admin.Id, "Selling banned items."));
        Assert.Equal(AccountActionOutcome.Done, await service.ReactivateAsync(target.Id, admin.Id));

        var saved = await db.Users.AsNoTracking().SingleAsync(u => u.Id == target.Id);
        Assert.Equal(AccountStatus.Active, saved.AccountStatus);
        var profile = await db.SellerProfiles.AsNoTracking().SingleAsync();
        Assert.Equal(SellerApplicationStatus.Suspended, profile.ApplicationStatus);
        var actions = (await db.AuditLogs.OrderBy(a => a.AuditLogId).ToListAsync()).Select(a => a.Action).ToArray();
        Assert.Equal(new[] { "AccountSuspended", "SellerSuspended", "AccountReactivated" }, actions);
    }

    [Fact]
    public async Task Invalid_transitions_and_unknown_ids_are_refused_without_audit_rows()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var sp = scope.ServiceProvider;
        await TestHost.EnsureRolesAsync(sp);
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var db = sp.GetRequiredService<ZitbabaDbContext>();
        var admin = await CreateUserAsync(users, "admin@test.local", "0970000001", RoleNames.Admin);
        var active = await CreateUserAsync(users, "active@test.local", "0970000002", RoleNames.Buyer);
        var deactivated = await CreateUserAsync(users, "gone@test.local", "0970000003", RoleNames.Buyer);
        deactivated.AccountStatus = AccountStatus.Deactivated;
        await db.SaveChangesAsync();
        var service = Service(sp);

        Assert.Equal(AccountActionOutcome.InvalidState, await service.ReactivateAsync(active.Id, admin.Id));
        Assert.Equal(AccountActionOutcome.InvalidState, await service.SuspendAsync(deactivated.Id, admin.Id, "A valid reason."));
        Assert.Equal(AccountActionOutcome.InvalidState, await service.ReactivateAsync(deactivated.Id, admin.Id));
        Assert.Equal(AccountActionOutcome.NotFound, await service.SuspendAsync(Guid.NewGuid(), admin.Id, "A valid reason."));
        Assert.Equal(AccountActionOutcome.NotFound, await service.ReactivateAsync(Guid.NewGuid(), admin.Id));

        Assert.Equal(AccountActionOutcome.Done, await service.SuspendAsync(active.Id, admin.Id, "A valid reason."));
        Assert.Equal(AccountActionOutcome.InvalidState, await service.SuspendAsync(active.Id, admin.Id, "A valid reason."));

        Assert.Single(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task List_searches_by_name_or_email_and_pages_20_per_page()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var sp = scope.ServiceProvider;
        await TestHost.EnsureRolesAsync(sp);
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        await CreateUserAsync(users, "admin@test.local", "0970000000", RoleNames.Admin, "Site Admin");
        await CreateUserAsync(users, "alice@test.local", "0970000001", RoleNames.Buyer, "Alice Banda");
        for (var i = 0; i < 23; i++)
            await CreateUserAsync(users, $"user{i:00}@other.local", $"09700001{i:00}", RoleNames.Buyer, $"Person {i:00}");
        var service = Service(sp);

        var all = await service.ListAsync(null, 1);
        Assert.Equal(25, all.TotalCount);
        Assert.Equal(20, all.Items.Count);
        Assert.Equal(5, (await service.ListAsync(null, 2)).Items.Count);
        Assert.Equal(2, (await service.ListAsync(null, 99)).Page);

        var byName = await service.ListAsync("ALICE", 1);
        Assert.Equal("alice@test.local", Assert.Single(byName.Items).Email);
        Assert.Equal(25 - 23, (await service.ListAsync("@test.local", 1)).TotalCount);
        Assert.Empty((await service.ListAsync("nobody-matches-this", 1)).Items);

        var admin = (await service.ListAsync("Site Admin", 1)).Items.Single();
        Assert.True(admin.IsAdmin);
        Assert.False(byName.Items[0].IsAdmin);
    }
}
