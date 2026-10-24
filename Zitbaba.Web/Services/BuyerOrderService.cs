using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Services;

public record OrderListItem(
    Guid OrderId,
    string OrderNumber,
    string StoreName,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    decimal Total,
    int ItemCount,
    DateTime CreatedAt);

public record OrderLineView(string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

public record OrderHistoryEntry(OrderStatus? From, OrderStatus To, DateTime At, string? Reason);

public record OrderDetail(
    Guid OrderId,
    string OrderNumber,
    Guid SellerProfileId,
    string StoreName,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    decimal Total,
    DateTime CreatedAt,
    IReadOnlyList<OrderLineView> Lines,
    IReadOnlyList<OrderHistoryEntry> History);

public interface IBuyerOrderService
{
    Task<PagedResult<OrderListItem>> ListAsync(Guid userId, int page);

    /// <summary>Null if the order does not exist or is not this buyer's. A real order owned by someone
    /// else also writes an AccessDenied audit row.</summary>
    Task<OrderDetail?> GetAsync(Guid userId, Guid orderId);
}

public class BuyerOrderService : IBuyerOrderService
{
    public const int PageSize = 10;

    private readonly ZitbabaDbContext _db;
    private readonly IAuditService _audit;

    public BuyerOrderService(ZitbabaDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<PagedResult<OrderListItem>> ListAsync(Guid userId, int page)
    {
        var query = _db.Orders.AsNoTracking().Where(o => o.BuyerUserId == userId);

        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, pages);

        var rows = await query
            .OrderByDescending(o => o.CreatedAt).ThenBy(o => o.OrderId)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(o => new
            {
                o.OrderId,
                o.OrderNumber,
                StoreName = o.SellerProfile!.StoreName,
                o.Status,
                o.PaymentStatus,
                o.TotalAmount,
                ItemCount = o.Items.Sum(i => i.Quantity),
                o.CreatedAt
            })
            .ToListAsync();

        var items = rows.Select(r => new OrderListItem(
            r.OrderId, r.OrderNumber, r.StoreName, r.Status, r.PaymentStatus,
            r.TotalAmount, r.ItemCount, r.CreatedAt)).ToList();

        return new PagedResult<OrderListItem>(items, page, PageSize, total);
    }

    public async Task<OrderDetail?> GetAsync(Guid userId, Guid orderId)
    {
        var row = await _db.Orders.AsNoTracking()
            .Where(o => o.OrderId == orderId && o.BuyerUserId == userId)
            .Select(o => new
            {
                o.OrderId,
                o.OrderNumber,
                o.SellerProfileId,
                StoreName = o.SellerProfile!.StoreName,
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
                await _audit.WriteAsync(userId, "AccessDenied", nameof(Order), orderId.ToString(),
                    new { reason = "NotOrderBuyer" });
            return null;
        }

        return new OrderDetail(
            row.OrderId, row.OrderNumber, row.SellerProfileId, row.StoreName,
            row.Status, row.PaymentStatus, row.TotalAmount, row.CreatedAt,
            row.Lines.Select(l => new OrderLineView(l.ProductName, l.UnitPrice, l.Quantity, l.LineTotal)).ToList(),
            row.History.Select(h => new OrderHistoryEntry(h.FromStatus, h.ToStatus, h.CreatedAt, h.Reason)).ToList());
    }
}
