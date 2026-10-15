using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Domain.Rules;

namespace Zitbaba.Web.Infrastructure.Security;

/// <summary>User must have the Seller role AND an Approved SellerProfile.</summary>
public class ApprovedSellerRequirement : IAuthorizationRequirement
{
}

public class ApprovedSellerHandler : AuthorizationHandler<ApprovedSellerRequirement>
{
    private readonly ZitbabaDbContext _db;

    public ApprovedSellerHandler(ZitbabaDbContext db) => _db = db;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ApprovedSellerRequirement requirement)
    {
        if (!context.User.IsInRole(RoleNames.Seller)) return;

        var idValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(idValue, out var userId)) return;

        // Read the status from the database every time so a suspension takes effect immediately.
        var status = await _db.SellerProfiles
            .AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => (SellerApplicationStatus?)s.ApplicationStatus)
            .FirstOrDefaultAsync();

        if (status is { } value && SellerRules.CanSell(value))
            context.Succeed(requirement);
    }
}
