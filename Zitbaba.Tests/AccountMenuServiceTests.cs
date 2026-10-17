using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Services;
using Xunit;

namespace Zitbaba.Tests;

public class AccountMenuServiceTests
{
    private static async Task<Guid> AddProfileAsync(IServiceScope scope, SellerApplicationStatus status)
    {
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var userId = Guid.NewGuid();
        db.SellerProfiles.Add(new SellerProfile
        {
            UserId = userId,
            StoreName = "Store " + userId,
            ApplicationStatus = status
        });
        await db.SaveChangesAsync();
        return userId;
    }

    [Fact]
    public async Task Signed_out_visitor_sees_no_account_links()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var menu = scope.ServiceProvider.GetRequiredService<IAccountMenuService>();

        var state = await menu.GetAsync(null, isAdmin: false);

        Assert.False(state.SignedIn);
        Assert.False(state.ShowBecomeSeller);
        Assert.False(state.ShowApplicationStatus);
        Assert.False(state.ShowSellerDashboard);
        Assert.False(state.ShowAdmin);
    }

    [Fact]
    public async Task Buyer_without_profile_sees_become_a_seller()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var menu = scope.ServiceProvider.GetRequiredService<IAccountMenuService>();

        var state = await menu.GetAsync(Guid.NewGuid(), isAdmin: false);

        Assert.True(state.SignedIn);
        Assert.True(state.ShowBecomeSeller);
        Assert.False(state.ShowApplicationStatus);
        Assert.False(state.ShowSellerDashboard);
    }

    [Fact]
    public async Task Admin_without_profile_does_not_see_become_a_seller()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var menu = scope.ServiceProvider.GetRequiredService<IAccountMenuService>();

        var state = await menu.GetAsync(Guid.NewGuid(), isAdmin: true);

        Assert.False(state.ShowBecomeSeller);
        Assert.True(state.ShowAdmin);
    }

    [Theory]
    [InlineData(SellerApplicationStatus.Pending)]
    [InlineData(SellerApplicationStatus.Rejected)]
    [InlineData(SellerApplicationStatus.Suspended)]
    public async Task Pending_rejected_or_suspended_sees_application_status_only(SellerApplicationStatus status)
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var userId = await AddProfileAsync(scope, status);
        var menu = scope.ServiceProvider.GetRequiredService<IAccountMenuService>();

        var state = await menu.GetAsync(userId, isAdmin: false);

        Assert.True(state.ShowApplicationStatus);
        Assert.False(state.ShowBecomeSeller);
        Assert.False(state.ShowSellerDashboard);
    }

    [Fact]
    public async Task Approved_seller_sees_dashboard_only()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var userId = await AddProfileAsync(scope, SellerApplicationStatus.Approved);
        var menu = scope.ServiceProvider.GetRequiredService<IAccountMenuService>();

        var state = await menu.GetAsync(userId, isAdmin: false);

        Assert.True(state.ShowSellerDashboard);
        Assert.False(state.ShowApplicationStatus);
        Assert.False(state.ShowBecomeSeller);
    }
}
