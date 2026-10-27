using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Infrastructure.Security;

namespace Zitbaba.Web.Services;

// Admin-only view of a user. No phone number or room number.
public record AdminUserListItem(
    Guid UserId, string FullName, string Email, AccountStatus Status, bool IsAdmin,
    SellerApplicationStatus? SellerStatus, DateTime CreatedAt);

public record AdminUserDetails(
    Guid UserId, string FullName, string Email, AccountStatus Status, bool IsAdmin,
    SellerApplicationStatus? SellerStatus, string? StoreName, DateTime CreatedAt);

public record AdminUsersIndexModel(string? Search, PagedResult<AdminUserListItem> Result);

public enum AccountActionOutcome
{
    Done,
    NotFound,
    InvalidState,
    ReasonRequired,
    CannotActOnSelf,
    CannotSuspendAdmin
}

public interface IAdminUserService
{
    Task<PagedResult<AdminUserListItem>> ListAsync(string? search, int page);
    Task<AdminUserDetails?> GetAsync(Guid userId);
    Task<AccountActionOutcome> SuspendAsync(Guid targetUserId, Guid adminUserId, string? reason);
    Task<AccountActionOutcome> ReactivateAsync(Guid targetUserId, Guid adminUserId);
}

public class AdminUserService : IAdminUserService
{
    public const int PageSize = 20;

    private readonly ZitbabaDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAuditService _audit;

    public AdminUserService(ZitbabaDbContext db, UserManager<ApplicationUser> users, IAuditService audit)
    {
        _db = db;
        _users = users;
        _audit = audit;
    }

    public async Task<PagedResult<AdminUserListItem>> ListAsync(string? search, int page)
    {
        var adminRoleId = await AdminRoleIdAsync();
        var query = _db.Users.AsNoTracking();

        var term = Clean(search)?.ToLowerInvariant();
        if (term is not null)
        {
            if (term.Length > 100) term = term[..100];
            query = query.Where(u =>
                u.FullName.ToLower().Contains(term)
                || (u.Email != null && u.Email.ToLower().Contains(term)));
        }

        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, pages);

        var items = await query
            .OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(u => new AdminUserListItem(
                u.Id,
                u.FullName,
                u.Email ?? string.Empty,
                u.AccountStatus,
                _db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == adminRoleId),
                _db.SellerProfiles.Where(s => s.UserId == u.Id)
                    .Select(s => (SellerApplicationStatus?)s.ApplicationStatus).FirstOrDefault(),
                u.CreatedAt))
            .ToListAsync();

        return new PagedResult<AdminUserListItem>(items, page, PageSize, total);
    }

    public async Task<AdminUserDetails?> GetAsync(Guid userId)
    {
        var adminRoleId = await AdminRoleIdAsync();

        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.FullName,
                Email = u.Email ?? string.Empty,
                u.AccountStatus,
                u.CreatedAt,
                IsAdmin = _db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == adminRoleId)
            })
            .FirstOrDefaultAsync();
        if (user is null) return null;

        var seller = await _db.SellerProfiles.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new { s.ApplicationStatus, s.StoreName })
            .FirstOrDefaultAsync();

        return new AdminUserDetails(
            user.Id, user.FullName, user.Email, user.AccountStatus, user.IsAdmin,
            seller?.ApplicationStatus, seller?.StoreName, user.CreatedAt);
    }

    public async Task<AccountActionOutcome> SuspendAsync(Guid targetUserId, Guid adminUserId, string? reason)
    {
        if (targetUserId == adminUserId) return AccountActionOutcome.CannotActOnSelf;

        var cleanReason = Clean(reason);
        if (cleanReason is null
            || cleanReason.Length < SellerApplicationLimits.ReviewReasonMin
            || cleanReason.Length > SellerApplicationLimits.ReviewReasonMax)
            return AccountActionOutcome.ReasonRequired;

        // Returning early without CommitAsync rolls everything back when the transaction is disposed.
        await using var tx = await _db.Database.BeginTransactionAsync();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId);
        if (user is null) return AccountActionOutcome.NotFound;
        if (user.AccountStatus != AccountStatus.Active) return AccountActionOutcome.InvalidState;
        if (await _users.IsInRoleAsync(user, RoleNames.Admin)) return AccountActionOutcome.CannotSuspendAdmin;

        var now = DateTime.UtcNow;
        user.AccountStatus = AccountStatus.Suspended;
        user.UpdatedAt = now;
        // A new security stamp invalidates the person's sign-in cookie at the next validation check.
        user.SecurityStamp = Guid.NewGuid().ToString();

        var seller = await _db.SellerProfiles.FirstOrDefaultAsync(s =>
            s.UserId == user.Id && s.ApplicationStatus == SellerApplicationStatus.Approved);
        if (seller is not null)
        {
            seller.ApplicationStatus = SellerApplicationStatus.Suspended;
            seller.ReviewedByUserId = adminUserId;
            seller.ReviewedAt = now;
            seller.ReviewReason = cleanReason;
        }

        _audit.Stage(adminUserId, "AccountSuspended", "User", user.Id.ToString(),
            new { userId = user.Id, sellerSuspended = seller is not null, reason = cleanReason });

        if (seller is not null)
        {
            _audit.Stage(adminUserId, "SellerSuspended", "SellerProfile", seller.SellerProfileId.ToString(),
                new { fromStatus = "Approved", toStatus = "Suspended", via = "AccountSuspended", reason = cleanReason });
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return AccountActionOutcome.Done;
    }

    public async Task<AccountActionOutcome> ReactivateAsync(Guid targetUserId, Guid adminUserId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId);
        if (user is null) return AccountActionOutcome.NotFound;
        if (user.AccountStatus != AccountStatus.Suspended) return AccountActionOutcome.InvalidState;

        user.AccountStatus = AccountStatus.Active;
        user.UpdatedAt = DateTime.UtcNow;

        _audit.Stage(adminUserId, "AccountReactivated", "User", user.Id.ToString(),
            new { userId = user.Id });

        await _db.SaveChangesAsync();
        return AccountActionOutcome.Done;
    }

    private Task<Guid> AdminRoleIdAsync() =>
        _db.Roles.Where(r => r.Name == RoleNames.Admin).Select(r => r.Id).FirstOrDefaultAsync();

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
