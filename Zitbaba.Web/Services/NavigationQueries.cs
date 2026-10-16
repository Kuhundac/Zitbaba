using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Services;

public record AccountMenuState(
    bool SignedIn,
    bool ShowBecomeSeller,
    bool ShowApplicationStatus,
    bool ShowSellerDashboard,
    bool ShowAdmin);

public interface IAccountMenuService
{
    Task<AccountMenuState> GetAsync(Guid? userId, bool isAdmin);
}

public class AccountMenuService : IAccountMenuService
{
    private readonly ZitbabaDbContext _db;

    public AccountMenuService(ZitbabaDbContext db) => _db = db;

    public async Task<AccountMenuState> GetAsync(Guid? userId, bool isAdmin)
    {
        if (userId is null)
            return new AccountMenuState(false, false, false, false, false);

        SellerApplicationStatus? status = await _db.SellerProfiles.AsNoTracking()
            .Where(s => s.UserId == userId.Value)
            .Select(s => (SellerApplicationStatus?)s.ApplicationStatus)
            .FirstOrDefaultAsync();

        // Approved -> dashboard. Pending, Rejected, Suspended -> application status.
        // No profile -> "Become a seller", except for admins.
        var showBecome = status is null && !isAdmin;
        var showDashboard = status == SellerApplicationStatus.Approved;
        var showStatus = status is not null && status != SellerApplicationStatus.Approved;

        return new AccountMenuState(true, showBecome, showStatus, showDashboard, isAdmin);
    }
}

public record AdminDashboardModel(int PendingApplications, int TotalProducts);

public interface IAdminDashboardService
{
    Task<AdminDashboardModel> GetAsync();
}

public class AdminDashboardService : IAdminDashboardService
{
    private readonly ZitbabaDbContext _db;

    public AdminDashboardService(ZitbabaDbContext db) => _db = db;

    public async Task<AdminDashboardModel> GetAsync()
    {
        var pending = await _db.SellerProfiles.AsNoTracking()
            .CountAsync(s => s.ApplicationStatus == SellerApplicationStatus.Pending);
        var products = await _db.Products.AsNoTracking()
            .CountAsync(p => p.Status != ProductStatus.Removed);
        return new AdminDashboardModel(pending, products);
    }
}
