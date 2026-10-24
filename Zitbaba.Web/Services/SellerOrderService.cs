using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Domain.Rules;

namespace Zitbaba.Web.Services;

public enum SellerOrderFilter
{
    Pending,
    InProgress,
    Closed,
    All
}

public record SellerOrderRow(
    Guid OrderId,
    string OrderNumber,
    string BuyerName,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    decimal Total,
    int ItemCount,
    DateTime CreatedAt);

public record SellerOrderDetail(
    Guid OrderId,
    string OrderNumber,
    string BuyerName,
    string? BuyerPhone,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    decimal Total,
    DateTime CreatedAt,
    IReadOnlyList<OrderLineView> Lines,
    IReadOnlyList<OrderHistoryEntry> History,
    IReadOnlyList<OrderStatus> Actions);

public interface ISellerOrderService
{
    Task<int> CountPendingAsync(Guid sellerUserId);
    Task<PagedResult<SellerOrderRow>> ListAsync(Guid sellerUserId, SellerOrderFilter filter, int page);

    /// <summary>Null if the order does not exist or belongs to another seller. A real order owned by
    /// someone else also writes an AccessDenied audit row.</summary>
    Task<SellerOrderDetail?> GetAsync(Guid sellerUserId, Guid orderId);
}

public class SellerOrderService : ISellerOrderService
{
    public const int PageSize = 10;

    private readonly ZitbabaDbContext _db;
    private readonly IAuditService _audit;

    public SellerOrderService(ZitbabaDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    private IQueryable<Order> MyOrders(Guid sellerUserId) =>
        _db.Orders.AsNoTracking().Where(o => o.SellerProfile!.UserId == sellerUserId);

    public Task<int> CountPendingAsync(Guid sellerUserId) =>
        MyOrders(sellerUserId).CountAsync(o => o.Status == OrderStatus.Pending);

    public async Task<PagedResult<SellerOrderRow>> ListAsync(Guid sellerUserId, SellerOrderFilter filter, int page)
    {
        var query = MyOrders(sellerUserId);
        query = filter switch
        {
            SellerOrderFilter.Pending => query.Where(o => o.Status == OrderStatus.Pending),
            SellerOrderFilter.InProgress => query.Where(o =>
                o.Status == OrderStatus.Accepted || o.Status == OrderStatus.ReadyForPickup),
            SellerOrderFilter.Closed => query.Where(o =>
                o.Status == OrderStatus.Rejected
                || o.Status == OrderStatus.Completed
                || o.Status == OrderStatus.CancelledByBuyer),
            _ => query
        };

        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, pages);

        // Work to do: oldest first. History: newest first.
        var ordered = filter is SellerOrderFilter.Pending or SellerOrderFilter.InProgress
            ? query.OrderBy(o => o.CreatedAt).ThenBy(o => o.OrderId)
            : query.OrderByDescending(o => o.CreatedAt).ThenBy(o => o.OrderId);

        var rows = await ordered
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(o => new
            {
                o.OrderId,
                o.OrderNumber,
                BuyerName = o.Buyer!.FullName,
                o.Status,
                o.PaymentStatus,
                o.TotalAmount,
                ItemCount = o.Items.Sum(i => i.Quantity),
                o.CreatedAt
            })
            .ToListAsync();

        var items = rows.Select(r => new SellerOrderRow(
            r.OrderId, r.OrderNumber, string.IsNullOrWhiteSpace(r.BuyerName) ? "Buyer" : r.BuyerName,
            r.Status, r.PaymentStatus, r.TotalAmount, r.ItemCount, r.CreatedAt)).ToList();

        return new PagedResult<SellerOrderRow>(items, page, PageSize, total);
    }

    public async Task<SellerOrderDetail?> GetAsync(Guid sellerUserId, Guid orderId)
    {
        var row = await MyOrders(sellerUserId)
            .Where(o => o.OrderId == orderId)
            .Select(o => new
            {
                o.OrderId,
                o.OrderNumber,
                BuyerName = o.Buyer!.FullName,
                BuyerPhone = o.Buyer!.Phone,
                o.Status,
                o.PaymentStatus,
                o.TotalAmount,
                o.CreatedAt,
                Lines = o.Items.OrderBy(i => i.OrderItemId)
                    .Select(i => new { i.ProductName, i.UnitPrice, i.Quantity, i.LineTotal }).ToList(),
                History = o.History.OrderBy(h => h.CreatedAt).ThenBy(h => h.OrderStatusHistoryId)
                    .Select(h => new { h.FromStatus, h.ToStatus, h.CreatedAt, h.Reason }).ToList()
            })
            .FirstOrDefaultAsync();

        if (row is null)
        {
            if (await _db.Orders.AsNoTracking().AnyAsync(o => o.OrderId == orderId))
                await _audit.WriteAsync(sellerUserId, "AccessDenied", nameof(Order), orderId.ToString(),
                    new { reason = "NotOrderSeller" });
            return null;
        }

        // The buyer's phone is only needed to arrange pickup, so it is hidden once the order is closed.
        var phone = OrderRules.IsFinal(row.Status) || string.IsNullOrWhiteSpace(row.BuyerPhone)
            ? null
            : row.BuyerPhone;

        return new SellerOrderDetail(
            row.OrderId, row.OrderNumber,
            string.IsNullOrWhiteSpace(row.BuyerName) ? "Buyer" : row.BuyerName,
            phone, row.Status, row.PaymentStatus, row.TotalAmount, row.CreatedAt,
            row.Lines.Select(l => new OrderLineView(l.ProductName, l.UnitPrice, l.Quantity, l.LineTotal)).ToList(),
            row.History.Select(h => new OrderHistoryEntry(h.FromStatus, h.ToStatus, h.CreatedAt, h.Reason)).ToList(),
            OrderRules.NextStatuses(row.Status, OrderActor.Seller));
    }
}
