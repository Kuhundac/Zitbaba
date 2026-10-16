using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Infrastructure.Security;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

public class SellerReviewTests
{
    private sealed record Seeded(ApplicationUser Applicant, ApplicationUser Admin, Guid ProfileId);

    private static async Task<ApplicationUser> CreateUserAsync(
        UserManager<ApplicationUser> users, string email, string phone, string role)
    {
        var user = new ApplicationUser { UserName = email, Email = email, FullName = "Test User", Phone = phone };
        Assert.True((await users.CreateAsync(user)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    private static async Task<Seeded> SeedPendingAsync(IServiceProvider sp)
    {
        await TestHost.EnsureRolesAsync(sp);
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var applications = sp.GetRequiredService<ISellerApplicationService>();
        var db = sp.GetRequiredService<ZitbabaDbContext>();

        var applicant = await CreateUserAsync(users, "buyer@test.local", "0970000001", RoleNames.Buyer);
        var admin = await CreateUserAsync(users, "admin@test.local", "0970000002", RoleNames.Admin);
        await applications.ApplyAsync(applicant.Id, new SellerApplicationRequest("Test Store", null, null));
        var profileId = (await db.SellerProfiles.SingleAsync()).SellerProfileId;
        return new Seeded(applicant, admin, profileId);
    }

    [Fact]
    public async Task Approve_adds_Seller_role_and_writes_audit_row()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var sp = scope.ServiceProvider;
        var s = await SeedPendingAsync(sp);
        var service = sp.GetRequiredService<ISellerApplicationService>();
        var db = sp.GetRequiredService<ZitbabaDbContext>();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();

        var outcome = await service.ApproveAsync(s.ProfileId, s.Admin.Id);

        Assert.Equal(SellerReviewOutcome.Done, outcome);
        var profile = await db.SellerProfiles.AsNoTracking().SingleAsync();
        Assert.Equal(SellerApplicationStatus.Approved, profile.ApplicationStatus);
        Assert.Equal(s.Admin.Id, profile.ReviewedByUserId);
        Assert.NotNull(profile.ReviewedAt);
        Assert.True(await users.IsInRoleAsync(s.Applicant, RoleNames.Seller));

        var audit = Assert.Single(await db.AuditLogs.ToListAsync());
        Assert.Equal("SellerApproved", audit.Action);
        Assert.Equal(s.Admin.Id, audit.ActorUserId);
        Assert.Equal(s.ProfileId.ToString(), audit.EntityId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no")]
    public async Task Reject_requires_a_reason_of_at_least_5_characters(string? reason)
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var sp = scope.ServiceProvider;
        var s = await SeedPendingAsync(sp);
        var service = sp.GetRequiredService<ISellerApplicationService>();
        var db = sp.GetRequiredService<ZitbabaDbContext>();

        var outcome = await service.RejectAsync(s.ProfileId, s.Admin.Id, reason);

        Assert.Equal(SellerReviewOutcome.ReasonRequired, outcome);
        Assert.Equal(SellerApplicationStatus.Pending,
            (await db.SellerProfiles.AsNoTracking().SingleAsync()).ApplicationStatus);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Reject_with_reason_sets_Rejected_and_writes_audit_row()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var sp = scope.ServiceProvider;
        var s = await SeedPendingAsync(sp);
        var service = sp.GetRequiredService<ISellerApplicationService>();
        var db = sp.GetRequiredService<ZitbabaDbContext>();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();

        var outcome = await service.RejectAsync(s.ProfileId, s.Admin.Id, "Please add more detail.");

        Assert.Equal(SellerReviewOutcome.Done, outcome);
        var profile = await db.SellerProfiles.AsNoTracking().SingleAsync();
        Assert.Equal(SellerApplicationStatus.Rejected, profile.ApplicationStatus);
        Assert.Equal("Please add more detail.", profile.ReviewReason);
        Assert.False(await users.IsInRoleAsync(s.Applicant, RoleNames.Seller));
        Assert.Equal("SellerRejected", Assert.Single(await db.AuditLogs.ToListAsync()).Action);
    }

    [Fact]
    public async Task Suspend_then_Reinstate_round_trips_and_is_audited()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var sp = scope.ServiceProvider;
        var s = await SeedPendingAsync(sp);
        var service = sp.GetRequiredService<ISellerApplicationService>();
        var db = sp.GetRequiredService<ZitbabaDbContext>();
        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();

        Assert.Equal(SellerReviewOutcome.Done, await service.ApproveAsync(s.ProfileId, s.Admin.Id));
        Assert.Equal(SellerReviewOutcome.ReasonRequired, await service.SuspendAsync(s.ProfileId, s.Admin.Id, "bad"));
        Assert.Equal(SellerReviewOutcome.Done, await service.SuspendAsync(s.ProfileId, s.Admin.Id, "Reported by buyers."));
        Assert.Equal(SellerApplicationStatus.Suspended,
            (await db.SellerProfiles.AsNoTracking().SingleAsync()).ApplicationStatus);

        Assert.Equal(SellerReviewOutcome.Done, await service.ReinstateAsync(s.ProfileId, s.Admin.Id));
        var profile = await db.SellerProfiles.AsNoTracking().SingleAsync();
        Assert.Equal(SellerApplicationStatus.Approved, profile.ApplicationStatus);
        Assert.Null(profile.ReviewReason);
        Assert.True(await users.IsInRoleAsync(s.Applicant, RoleNames.Seller));

        var actions = (await db.AuditLogs.OrderBy(a => a.AuditLogId).ToListAsync()).Select(a => a.Action);
        Assert.Equal(new[] { "SellerApproved", "SellerSuspended", "SellerReinstated" }, actions);
    }

    [Fact]
    public async Task Invalid_transitions_are_refused_and_not_audited()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var sp = scope.ServiceProvider;
        var s = await SeedPendingAsync(sp);
        var service = sp.GetRequiredService<ISellerApplicationService>();
        var db = sp.GetRequiredService<ZitbabaDbContext>();

        // A Pending profile cannot be suspended or reinstated.
        Assert.Equal(SellerReviewOutcome.InvalidState, await service.SuspendAsync(s.ProfileId, s.Admin.Id, "Some valid reason"));
        Assert.Equal(SellerReviewOutcome.InvalidState, await service.ReinstateAsync(s.ProfileId, s.Admin.Id));

        // An Approved profile cannot be approved again, and unknown ids are NotFound.
        Assert.Equal(SellerReviewOutcome.Done, await service.ApproveAsync(s.ProfileId, s.Admin.Id));
        Assert.Equal(SellerReviewOutcome.InvalidState, await service.ApproveAsync(s.ProfileId, s.Admin.Id));
        Assert.Equal(SellerReviewOutcome.NotFound, await service.ApproveAsync(Guid.NewGuid(), s.Admin.Id));

        Assert.Single(await db.AuditLogs.ToListAsync()); // only the one successful approval
    }
}
