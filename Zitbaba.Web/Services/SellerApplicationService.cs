using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Infrastructure.Security;

namespace Zitbaba.Web.Services;

public static class SellerApplicationLimits
{
    public const int StoreNameMax = 120;
    public const int TextMax = 1000;
    public const int ReviewReasonMin = 5;
    public const int ReviewReasonMax = 500; // matches the ReviewReason column size
}

public record SellerApplicationRequest(string StoreName, string? Description, string? ApplicationNotes);

public record SellerApplicationInfo(
    Guid SellerProfileId,
    string StoreName,
    string? Description,
    string? ApplicationNotes,
    SellerApplicationStatus Status,
    string? ReviewReason);

public enum SellerApplyOutcome
{
    Created,
    Resubmitted,
    AlreadyPending,
    AlreadyApproved,
    Suspended,
    StoreNameTaken,
    Invalid
}

public enum SellerReviewOutcome
{
    Done,
    NotFound,
    InvalidState,
    ReasonRequired,
    RoleFailed
}

public interface ISellerApplicationService
{
    Task<SellerApplicationInfo?> GetStatusAsync(Guid userId);
    Task<SellerApplyOutcome> ApplyAsync(Guid userId, SellerApplicationRequest request);

    Task<SellerReviewOutcome> ApproveAsync(Guid sellerProfileId, Guid adminUserId);
    Task<SellerReviewOutcome> RejectAsync(Guid sellerProfileId, Guid adminUserId, string? reason);
    Task<SellerReviewOutcome> SuspendAsync(Guid sellerProfileId, Guid adminUserId, string? reason);
    Task<SellerReviewOutcome> ReinstateAsync(Guid sellerProfileId, Guid adminUserId);
}

public class SellerApplicationService : ISellerApplicationService
{
    private readonly ZitbabaDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly IAuditService _audit;

    public SellerApplicationService(
        ZitbabaDbContext db, UserManager<ApplicationUser> users, IAuditService audit)
    {
        _db = db;
        _users = users;
        _audit = audit;
    }

    public Task<SellerApplicationInfo?> GetStatusAsync(Guid userId) =>
        _db.SellerProfiles.AsNoTracking()
            .Where(s => s.UserId == userId)
            .Select(s => new SellerApplicationInfo(
                s.SellerProfileId, s.StoreName, s.Description, s.ApplicationNotes,
                s.ApplicationStatus, s.ReviewReason))
            .FirstOrDefaultAsync();

    public async Task<SellerApplyOutcome> ApplyAsync(Guid userId, SellerApplicationRequest request)
    {
        var storeName = (request.StoreName ?? string.Empty).Trim();
        var description = Clean(request.Description);
        var notes = Clean(request.ApplicationNotes);

        if (storeName.Length == 0
            || storeName.Length > SellerApplicationLimits.StoreNameMax
            || (description?.Length ?? 0) > SellerApplicationLimits.TextMax
            || (notes?.Length ?? 0) > SellerApplicationLimits.TextMax)
            return SellerApplyOutcome.Invalid;

        var profile = await _db.SellerProfiles.FirstOrDefaultAsync(s => s.UserId == userId);

        if (profile is not null)
        {
            switch (profile.ApplicationStatus)
            {
                case SellerApplicationStatus.Pending: return SellerApplyOutcome.AlreadyPending;
                case SellerApplicationStatus.Approved: return SellerApplyOutcome.AlreadyApproved;
                case SellerApplicationStatus.Suspended: return SellerApplyOutcome.Suspended;
            }
            // Only Rejected profiles continue: they are updated and set back to Pending.
        }

        if (await IsStoreNameTakenAsync(storeName, userId))
            return SellerApplyOutcome.StoreNameTaken;

        var isNew = profile is null;
        if (profile is null)
        {
            profile = new SellerProfile { UserId = userId };
            _db.SellerProfiles.Add(profile);
        }

        profile.StoreName = storeName;
        profile.Description = description;
        profile.ApplicationNotes = notes;
        profile.ApplicationStatus = SellerApplicationStatus.Pending;
        profile.ReviewedByUserId = null;
        profile.ReviewedAt = null;
        profile.ReviewReason = null;

        try
        {
            await _db.SaveChangesAsync();
            return isNew ? SellerApplyOutcome.Created : SellerApplyOutcome.Resubmitted;
        }
        catch (DbUpdateException)
        {
            // Double-submit or two people picking the same name at the same moment.
            _db.ChangeTracker.Clear();
            if (isNew && await _db.SellerProfiles.AnyAsync(s => s.UserId == userId))
                return SellerApplyOutcome.AlreadyPending;
            if (await IsStoreNameTakenAsync(storeName, userId))
                return SellerApplyOutcome.StoreNameTaken;
            throw;
        }
    }

    public Task<SellerReviewOutcome> ApproveAsync(Guid sellerProfileId, Guid adminUserId) =>
        ReviewAsync(sellerProfileId, adminUserId,
            from: SellerApplicationStatus.Pending, to: SellerApplicationStatus.Approved,
            action: "SellerApproved", reason: null, grantSellerRole: true);

    public Task<SellerReviewOutcome> RejectAsync(Guid sellerProfileId, Guid adminUserId, string? reason) =>
        ReviewAsync(sellerProfileId, adminUserId,
            from: SellerApplicationStatus.Pending, to: SellerApplicationStatus.Rejected,
            action: "SellerRejected", reason: reason, grantSellerRole: false, reasonRequired: true);

    public Task<SellerReviewOutcome> SuspendAsync(Guid sellerProfileId, Guid adminUserId, string? reason) =>
        ReviewAsync(sellerProfileId, adminUserId,
            from: SellerApplicationStatus.Approved, to: SellerApplicationStatus.Suspended,
            action: "SellerSuspended", reason: reason, grantSellerRole: false, reasonRequired: true);

    public Task<SellerReviewOutcome> ReinstateAsync(Guid sellerProfileId, Guid adminUserId) =>
        ReviewAsync(sellerProfileId, adminUserId,
            from: SellerApplicationStatus.Suspended, to: SellerApplicationStatus.Approved,
            action: "SellerReinstated", reason: null, grantSellerRole: true);

    /// <summary>Status change, role change and audit row succeed or fail together.</summary>
    private async Task<SellerReviewOutcome> ReviewAsync(
        Guid sellerProfileId, Guid adminUserId,
        SellerApplicationStatus from, SellerApplicationStatus to,
        string action, string? reason, bool grantSellerRole, bool reasonRequired = false)
    {
        var cleanReason = Clean(reason);
        if (reasonRequired
            && (cleanReason is null
                || cleanReason.Length < SellerApplicationLimits.ReviewReasonMin
                || cleanReason.Length > SellerApplicationLimits.ReviewReasonMax))
            return SellerReviewOutcome.ReasonRequired;

        // Returning early without CommitAsync rolls everything back when the transaction is disposed.
        await using var tx = await _db.Database.BeginTransactionAsync();

        var profile = await _db.SellerProfiles.FirstOrDefaultAsync(s => s.SellerProfileId == sellerProfileId);
        if (profile is null) return SellerReviewOutcome.NotFound;
        if (profile.ApplicationStatus != from) return SellerReviewOutcome.InvalidState;

        profile.ApplicationStatus = to;
        profile.ReviewedByUserId = adminUserId;
        profile.ReviewedAt = DateTime.UtcNow;
        profile.ReviewReason = cleanReason;

        if (grantSellerRole)
        {
            var user = await _users.FindByIdAsync(profile.UserId.ToString());
            if (user is null) return SellerReviewOutcome.NotFound;

            if (!await _users.IsInRoleAsync(user, RoleNames.Seller))
            {
                var added = await _users.AddToRoleAsync(user, RoleNames.Seller);
                if (!added.Succeeded) return SellerReviewOutcome.RoleFailed;
            }
        }

        _audit.Stage(adminUserId, action, "SellerProfile", profile.SellerProfileId.ToString(),
            new { fromStatus = from.ToString(), toStatus = to.ToString(), reason = cleanReason });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return SellerReviewOutcome.Done;
    }

    private Task<bool> IsStoreNameTakenAsync(string storeName, Guid userId)
    {
        var lowered = storeName.ToLower();
        return _db.SellerProfiles.AnyAsync(s => s.UserId != userId && s.StoreName.ToLower() == lowered);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
