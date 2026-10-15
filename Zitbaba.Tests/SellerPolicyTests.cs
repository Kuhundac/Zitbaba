using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Infrastructure.Security;

namespace Zitbaba.Tests;

public class SellerPolicyTests
{
    private static ClaimsPrincipal PrincipalFor(Guid userId, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static async Task<bool> EvaluateAsync(SellerApplicationStatus status, bool hasSellerRole)
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();

        var userId = Guid.NewGuid();
        db.SellerProfiles.Add(new SellerProfile
        {
            UserId = userId,
            StoreName = "Test Store",
            ApplicationStatus = status
        });
        await db.SaveChangesAsync();

        var requirement = new ApprovedSellerRequirement();
        var principal = hasSellerRole
            ? PrincipalFor(userId, RoleNames.Seller)
            : PrincipalFor(userId, RoleNames.Buyer);
        var context = new AuthorizationHandlerContext(new[] { requirement }, principal, null);

        await new ApprovedSellerHandler(db).HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task Pending_seller_fails_RequireApprovedSeller() =>
        Assert.False(await EvaluateAsync(SellerApplicationStatus.Pending, hasSellerRole: true));

    [Fact]
    public async Task Approved_seller_passes_RequireApprovedSeller() =>
        Assert.True(await EvaluateAsync(SellerApplicationStatus.Approved, hasSellerRole: true));

    [Fact]
    public async Task Approved_profile_without_Seller_role_fails() =>
        Assert.False(await EvaluateAsync(SellerApplicationStatus.Approved, hasSellerRole: false));
}
