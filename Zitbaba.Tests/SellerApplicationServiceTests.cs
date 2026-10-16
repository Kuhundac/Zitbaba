using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

public class SellerApplicationServiceTests
{
    private static SellerApplicationRequest Request(string name = "Kc Snacks") =>
        new(name, "Snacks and drinks", "Sodas and crisps");

    [Fact]
    public async Task Apply_creates_a_Pending_profile()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISellerApplicationService>();
        var userId = Guid.NewGuid();

        var outcome = await service.ApplyAsync(userId, Request());
        var info = await service.GetStatusAsync(userId);

        Assert.Equal(SellerApplyOutcome.Created, outcome);
        Assert.Equal(SellerApplicationStatus.Pending, info!.Status);
    }

    [Fact]
    public async Task Second_apply_while_Pending_is_rejected()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISellerApplicationService>();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var userId = Guid.NewGuid();

        await service.ApplyAsync(userId, Request());
        var second = await service.ApplyAsync(userId, Request("Another name"));

        Assert.Equal(SellerApplyOutcome.AlreadyPending, second);
        Assert.Equal(1, await db.SellerProfiles.CountAsync());
    }

    [Fact]
    public async Task Rejected_applicant_can_resubmit_on_the_same_profile()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISellerApplicationService>();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var userId = Guid.NewGuid();

        await service.ApplyAsync(userId, Request());
        var profile = await db.SellerProfiles.SingleAsync();
        profile.ApplicationStatus = SellerApplicationStatus.Rejected;
        profile.ReviewReason = "Please add more detail.";
        await db.SaveChangesAsync();

        var outcome = await service.ApplyAsync(userId, Request("Kc Snacks Plus"));
        var info = await service.GetStatusAsync(userId);

        Assert.Equal(SellerApplyOutcome.Resubmitted, outcome);
        Assert.Equal(SellerApplicationStatus.Pending, info!.Status);
        Assert.Equal("Kc Snacks Plus", info.StoreName);
        Assert.Null(info.ReviewReason);
        Assert.Equal(1, await db.SellerProfiles.CountAsync());
    }

    [Fact]
    public async Task Suspended_seller_cannot_reapply()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISellerApplicationService>();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var userId = Guid.NewGuid();

        await service.ApplyAsync(userId, Request());
        (await db.SellerProfiles.SingleAsync()).ApplicationStatus = SellerApplicationStatus.Suspended;
        await db.SaveChangesAsync();

        var outcome = await service.ApplyAsync(userId, Request("New name"));
        var info = await service.GetStatusAsync(userId);

        Assert.Equal(SellerApplyOutcome.Suspended, outcome);
        Assert.Equal(SellerApplicationStatus.Suspended, info!.Status);
    }

    [Fact]
    public async Task Store_name_must_be_unique_ignoring_case()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISellerApplicationService>();

        await service.ApplyAsync(Guid.NewGuid(), Request("Kc Snacks"));
        var outcome = await service.ApplyAsync(Guid.NewGuid(), Request("KC SNACKS"));

        Assert.Equal(SellerApplyOutcome.StoreNameTaken, outcome);
    }
}