using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Infrastructure.Extensions;
using Zitbaba.Web.Domain.Rules;

namespace Zitbaba.Web.Services;

public enum TransitionOutcome
{
    Ok,
    NotFound,
    NotAllowed,
    ReasonRequired,
    Changed
}

public record TransitionResult(TransitionOutcome Outcome, string Message, OrderStatus? NewStatus = null)
{
    public bool Succeeded => Outcome == TransitionOutcome.Ok;
}

public interface IOrderTransitionService
{
    Task<TransitionResult> SellerAcceptAsync(Guid sellerUserId, Guid orderId);
    Task<TransitionResult> SellerRejectAsync(Guid sellerUserId, Guid orderId, string? reason);
    Task<TransitionResult> SellerMarkReadyAsync(Guid sellerUserId, Guid orderId);
    Task<TransitionResult> SellerCompleteAsync(Guid sellerUserId, Guid orderId);
    Task<TransitionResult> BuyerCancelAsync(Guid buyerUserId, Guid orderId, string? reason);
}

public class OrderTransitionService : IOrderTransitionService
{
    private const int ReasonMax = 500;

    private readonly ZitbabaDbContext _db;
    private readonly IAuditService _audit;

    public OrderTransitionService(ZitbabaDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public Task<TransitionResult> SellerAcceptAsync(Guid sellerUserId, Guid orderId) =>
        RunAsync(sellerUserId, OrderActor.Seller, orderId, OrderStatus.Accepted, null);

    public Task<TransitionResult> SellerRejectAsync(Guid sellerUserId, Guid orderId, string? reason) =>
        RunAsync(sellerUserId, OrderActor.Seller, orderId, OrderStatus.Rejected, reason);

    public Task<TransitionResult> SellerMarkReadyAsync(Guid sellerUserId, Guid orderId) =>
        RunAsync(sellerUserId, OrderActor.Seller, orderId, OrderStatus.ReadyForPickup, null);

    public Task<TransitionResult> SellerCompleteAsync(Guid sellerUserId, Guid orderId) =>
        RunAsync(sellerUserId, OrderActor.Seller, orderId, OrderStatus.Completed, null);

    public Task<TransitionResult> BuyerCancelAsync(Guid buyerUserId, Guid orderId, string? reason) =>
        RunAsync(buyerUserId, OrderActor.Buyer, orderId, OrderStatus.CancelledByBuyer, reason);

    private async Task<TransitionResult> RunAsync(
        Guid actorUserId, OrderActor actor, Guid orderId, OrderStatus to, string? reason)
    {
        var head = await _db.Orders.AsNoTracking()
            .Where(o => o.OrderId == orderId)
            .Select(o => new { o.Status, o.BuyerUserId, SellerUserId = o.SellerProfile!.UserId })
            .FirstOrDefaultAsync();

        if (head is null)
            return new TransitionResult(TransitionOutcome.NotFound, "That order was not found.");

        var isParty = actor == OrderActor.Seller
            ? head.SellerUserId == actorUserId
            : head.BuyerUserId == actorUserId;
        if (!isParty)
        {
            await _audit.WriteAsync(actorUserId, "AccessDenied", nameof(Order), orderId.ToString(),
                new { reason = "NotOrderParty", attempted = to.ToString() });
            return new TransitionResult(TransitionOutcome.NotFound, "That order was not found.");
        }

        // BR-002: a suspended (or otherwise not approved) seller cannot accept new orders.
        if (actor == OrderActor.Seller && to == OrderStatus.Accepted)
        {
            var sellerApproved = await _db.SellerProfiles.AsNoTracking().AnyAsync(s =>
                s.UserId == actorUserId && s.ApplicationStatus == SellerApplicationStatus.Approved);
            if (!sellerApproved)
                return new TransitionResult(TransitionOutcome.NotAllowed,
                    "Your seller account cannot accept new orders right now."); // SellerNotApproved
        }

        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reason is { Length: > ReasonMax }) reason = reason[..ReasonMax];

        if (OrderRules.RequiresReason(to) && reason is null)
            return new TransitionResult(TransitionOutcome.ReasonRequired,
                "Please give a reason so the buyer knows why.");

        if (!OrderRules.CanTransition(head.Status, to, actor))
            return new TransitionResult(TransitionOutcome.NotAllowed,
                $"This order cannot be changed that way. It is currently: {OrderLabels.Status(head.Status).ToLowerInvariant()}.");

        var from = head.Status;
        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(() => ApplyAsync(actorUserId, orderId, from, to, reason));
    }

    private async Task<TransitionResult> ApplyAsync(
        Guid actorUserId, Guid orderId, OrderStatus from, OrderStatus to, string? reason)
    {
        _db.ChangeTracker.Clear();
        var now = DateTime.UtcNow;
        var changed = new TransitionResult(TransitionOutcome.Changed,
            "This order was just changed by someone else. Please check its status.");

        await using var tx = await _db.Database.BeginTransactionAsync();

        if (_db.Database.IsRelational())
        {
            // One statement changes the status only if it is still what we read. If a buyer
            // cancels while the seller accepts, exactly one of them gets a row back.
            var query = _db.Orders.Where(o => o.OrderId == orderId && o.Status == from);
            var rows = OrderRules.MarksPaid(to)
                ? await query.ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Status, to)
                    .SetProperty(o => o.PaymentStatus, PaymentStatus.Paid)
                    .SetProperty(o => o.UpdatedAt, now))
                : await query.ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Status, to)
                    .SetProperty(o => o.UpdatedAt, now));

            if (rows != 1)
            {
                await tx.RollbackAsync();
                return changed;
            }

            if (OrderRules.RestoresStock(to))
            {
                var items = await _db.OrderItems.AsNoTracking()
                    .Where(i => i.OrderId == orderId)
                    .Select(i => new { i.ProductId, i.Quantity })
                    .ToListAsync();

                foreach (var item in items.OrderBy(i => i.ProductId))
                {
                    var productId = item.ProductId;
                    var quantity = item.Quantity;
                    await _db.Products
                        .Where(p => p.ProductId == productId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(p => p.QuantityAvailable, p => p.QuantityAvailable + quantity)
                            .SetProperty(p => p.UpdatedAt, now));
                }
            }
        }
        else
        {
            // Non-relational providers (unit tests) cannot run set-based updates. The real atomic
            // path above is covered by the SQL Server tests.
            var order = await _db.Orders.Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.OrderId == orderId && o.Status == from);
            if (order is null)
            {
                await tx.RollbackAsync();
                return changed;
            }

            order.Status = to;
            order.UpdatedAt = now;
            if (OrderRules.MarksPaid(to)) order.PaymentStatus = PaymentStatus.Paid;

            if (OrderRules.RestoresStock(to))
            {
                foreach (var item in order.Items)
                {
                    var product = await _db.Products.FirstOrDefaultAsync(p => p.ProductId == item.ProductId);
                    if (product is null) continue;
                    product.QuantityAvailable += item.Quantity;
                    product.UpdatedAt = now;
                }
            }
        }

        _db.OrderStatusHistory.Add(new OrderStatusHistory
        {
            OrderId = orderId,
            FromStatus = from,
            ToStatus = to,
            ActorUserId = actorUserId,
            Reason = reason,
            CreatedAt = now
        });

        // Ids and statuses only. The free-text reason lives in the history row, not the audit log.
        _audit.Stage(actorUserId, AuditActionFor(to), nameof(Order), orderId.ToString(),
            new { orderId, from = from.ToString(), to = to.ToString() });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return new TransitionResult(TransitionOutcome.Ok, MessageFor(to), to);
    }

    private static string AuditActionFor(OrderStatus to) => to switch
    {
        OrderStatus.Accepted => "OrderAccepted",
        OrderStatus.Rejected => "OrderRejected",
        OrderStatus.ReadyForPickup => "OrderReadyForPickup",
        OrderStatus.Completed => "OrderCompleted",
        OrderStatus.CancelledByBuyer => "OrderCancelled",
        _ => "OrderStatusChanged"
    };

    private static string MessageFor(OrderStatus to) => to switch
    {
        OrderStatus.Accepted => "Order accepted.",
        OrderStatus.Rejected => "Order rejected. The stock was put back.",
        OrderStatus.ReadyForPickup => "Order marked ready for pickup.",
        OrderStatus.Completed => "Order completed. Cash received.",
        OrderStatus.CancelledByBuyer => "Order cancelled. The stock was put back.",
        _ => "Order updated."
    };
}

