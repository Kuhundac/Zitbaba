using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Services;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}

// No phone number or room number is ever included here.
public record SellerApprovalListItem(
    Guid SellerProfileId, string StoreName, string ApplicantName, string ApplicantEmail,
    SellerApplicationStatus Status, DateTime CreatedAt);

public record SellerApprovalDetails(
    Guid SellerProfileId, string StoreName, string? Description, string? ApplicationNotes,
    string ApplicantName, string ApplicantEmail, SellerApplicationStatus Status,
    string? ReviewReason, DateTime CreatedAt, DateTime? ReviewedAt);

public record SellerApprovalsIndexModel(string Filter, PagedResult<SellerApprovalListItem> Result);

public interface ISellerApprovalQueryService
{
    Task<PagedResult<SellerApprovalListItem>> ListAsync(SellerApplicationStatus? status, int page, int pageSize);
    Task<SellerApprovalDetails?> GetDetailsAsync(Guid sellerProfileId);
}

public class SellerApprovalQueryService : ISellerApprovalQueryService
{
    private readonly ZitbabaDbContext _db;

    public SellerApprovalQueryService(ZitbabaDbContext db) => _db = db;

    public async Task<PagedResult<SellerApprovalListItem>> ListAsync(
        SellerApplicationStatus? status, int page, int pageSize)
    {
        var query = _db.SellerProfiles.AsNoTracking();
        if (status is not null)
        {
            var wanted = status.Value;
            query = query.Where(s => s.ApplicationStatus == wanted);
        }

        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        var items = await query
            .OrderByDescending(s => s.CreatedAt).ThenBy(s => s.SellerProfileId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new SellerApprovalListItem(
                s.SellerProfileId, s.StoreName, s.User!.FullName, s.User!.Email ?? string.Empty,
                s.ApplicationStatus, s.CreatedAt))
            .ToListAsync();

        return new PagedResult<SellerApprovalListItem>(items, page, pageSize, total);
    }

    public Task<SellerApprovalDetails?> GetDetailsAsync(Guid sellerProfileId) =>
        _db.SellerProfiles.AsNoTracking()
            .Where(s => s.SellerProfileId == sellerProfileId)
            .Select(s => new SellerApprovalDetails(
                s.SellerProfileId, s.StoreName, s.Description, s.ApplicationNotes,
                s.User!.FullName, s.User!.Email ?? string.Empty, s.ApplicationStatus,
                s.ReviewReason, s.CreatedAt, s.ReviewedAt))
            .FirstOrDefaultAsync();
}
