using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Services;

public enum CheckoutOutcome
{
    Placed,
    EmptyCart,
    NotAvailable,
    OwnProduct,
    NotEnoughStock,
    PriceChanged,
    Invalid
}

public record CheckoutResult(
    CheckoutOutcome Outcome,
    string Message,
    Guid? OrderId = null,
    string? OrderNumber = null,
    bool AlreadyPlaced = false)
{
    public bool Succeeded => Outcome == CheckoutOutcome.Placed;
}

public interface ICheckoutService
{
    /// <param name="submissionKey">One-time key from the checkout form. A repeat returns the same order.</param>
    /// <param name="expectedTotal">The total the buyer saw. If prices moved, the order is refused.</param>
    Task<CheckoutResult> PlaceOrderAsync(Guid userId, Guid submissionKey, decimal expectedTotal);
}

public class CheckoutService : ICheckoutService
{
    private const int MaxAttempts = 3;
    private const int ProductNameMax = 160;

    private readonly ZitbabaDbContext _db;
    private readonly IAuditService _audit;

    public CheckoutService(ZitbabaDbContext db, IAuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<CheckoutResult> PlaceOrderAsync(Guid userId, Guid submissionKey, decimal expectedTotal)
    {
        if (submissionKey == Guid.Empty)
            return new CheckoutResult(CheckoutOutcome.Invalid, "This checkout form is not valid. Please try again.");

        var existing = await FindExistingAsync(userId, submissionKey);
        if (existing is not null) return existing;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var strategy = _db.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync(() => TryPlaceAsync(userId, submissionKey, expectedTotal));
            }
            catch (DbUpdateException) when (attempt < MaxAttempts)
            {
                // A unique index stopped us: a double click (same key) or an order-number clash.
                _db.ChangeTracker.Clear();
                var again = await FindExistingAsync(userId, submissionKey);
                if (again is not null) return again;
                // Otherwise it was an order-number clash: loop and try again with a new number.
            }
        }
    }

    private async Task<CheckoutResult> TryPlaceAsync(Guid userId, Guid submissionKey, decimal expectedTotal)
    {
        _db.ChangeTracker.Clear();

        var cart = await _db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == userId);
        if (cart is null || cart.Items.Count == 0 || cart.SellerProfileId is null)
        {
            // A twin request (double click) may have just placed this order and emptied the cart.
            var done = await FindExistingAsync(userId, submissionKey);
            return done ?? new CheckoutResult(CheckoutOutcome.EmptyCart, "Your cart is empty.");
        }

        var sellerId = cart.SellerProfileId.Value;
        var seller = await _db.SellerProfiles.AsNoTracking()
            .Where(s => s.SellerProfileId == sellerId)
            .Select(s => new { s.UserId, s.ApplicationStatus })
            .FirstOrDefaultAsync();
        if (seller is null || seller.ApplicationStatus != SellerApplicationStatus.Approved)
            return new CheckoutResult(CheckoutOutcome.NotAvailable,
                "This seller is not available right now. Please review your cart.");
        if (seller.UserId == userId)
            return new CheckoutResult(CheckoutOutcome.OwnProduct, "You cannot buy your own products.");

        await using var tx = await _db.Database.BeginTransactionAsync();

        // Fixed order (by product) so two buyers never lock the same rows in opposite orders.
        var lines = cart.Items.OrderBy(i => i.ProductId).ToList();

        // 1. Take the stock. Any line that cannot be taken cancels everything.
        foreach (var line in lines)
        {
            if (await TakeStockAsync(line.ProductId, sellerId, line.Quantity)) continue;

            await tx.RollbackAsync();
            return await ExplainShortageAsync(userId, submissionKey, line);
        }

        // 2. Read prices AFTER the stock update: the row lock stops a seller changing them now.
        var ids = lines.Select(l => l.ProductId).ToList();
        var products = await _db.Products.AsNoTracking()
            .Where(p => ids.Contains(p.ProductId))
            .Select(p => new { p.ProductId, p.Name, p.Price })
            .ToListAsync();

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var product = products.FirstOrDefault(p => p.ProductId == line.ProductId);
            if (product is null)
            {
                await tx.RollbackAsync();
                _db.ChangeTracker.Clear();
                return new CheckoutResult(CheckoutOutcome.NotAvailable,
                    "An item in your cart is no longer available. Please review your cart.");
            }

            items.Add(new OrderItem
            {
                ProductId = product.ProductId,
                ProductName = product.Name.Length > ProductNameMax ? product.Name[..ProductNameMax] : product.Name,
                UnitPrice = product.Price,
                Quantity = line.Quantity,
                LineTotal = product.Price * line.Quantity
            });
        }

        var total = items.Sum(i => i.LineTotal);
        if (total != expectedTotal)
        {
            await tx.RollbackAsync();
            _db.ChangeTracker.Clear();
            return new CheckoutResult(CheckoutOutcome.PriceChanged,
                "Prices changed since you opened checkout. Please review your cart and try again.");
        }

        // 3. Write the order, its history and the audit row, empty the cart, commit together.
        var now = DateTime.UtcNow;
        var order = new Order
        {
            OrderNumber = NewOrderNumber(),
            BuyerUserId = userId,
            SellerProfileId = sellerId,
            Status = OrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            PaymentMethod = PaymentMethod.Cash,
            FulfilmentMethod = FulfilmentMethod.Pickup,
            TotalAmount = total,
            SubmissionKey = submissionKey,
            CreatedAt = now,
            UpdatedAt = now
        };
        foreach (var item in items) order.Items.Add(item);
        order.History.Add(new OrderStatusHistory
        {
            FromStatus = null,
            ToStatus = OrderStatus.Pending,
            ActorUserId = userId,
            Reason = "Order placed",
            CreatedAt = now
        });
        _db.Orders.Add(order);

        _db.CartItems.RemoveRange(cart.Items.ToList());
        cart.SellerProfileId = null;
        cart.UpdatedAt = now;

        _audit.Stage(userId, "OrderPlaced", nameof(Order), order.OrderId.ToString(),
            new
            {
                orderId = order.OrderId,
                orderNumber = order.OrderNumber,
                sellerProfileId = sellerId,
                total,
                itemCount = items.Count
            });

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return new CheckoutResult(CheckoutOutcome.Placed,
            $"Order {order.OrderNumber} placed.", order.OrderId, order.OrderNumber);
    }

    /// <summary>Atomically takes stock for one line. True only if exactly this many units were available.</summary>
    private async Task<bool> TakeStockAsync(Guid productId, Guid sellerId, int quantity)
    {
        var now = DateTime.UtcNow;

        if (_db.Database.IsRelational())
        {
            // One SQL statement: check and subtract together, so two buyers can never both win the last unit.
            var rows = await _db.Products
                .Where(p => p.ProductId == productId
                            && p.SellerProfileId == sellerId
                            && p.Status == ProductStatus.Active
                            && p.QuantityAvailable >= quantity)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.QuantityAvailable, p => p.QuantityAvailable - quantity)
                    .SetProperty(p => p.UpdatedAt, now));
            return rows == 1;
        }

        // Non-relational providers (unit tests) cannot run set-based updates. The real atomic
        // path above is covered by the SQL Server tests.
        var product = await _db.Products.FirstOrDefaultAsync(p =>
            p.ProductId == productId
            && p.SellerProfileId == sellerId
            && p.Status == ProductStatus.Active
            && p.QuantityAvailable >= quantity);
        if (product is null) return false;

        product.QuantityAvailable -= quantity;
        product.UpdatedAt = now;
        return true;
    }

    private async Task<CheckoutResult> ExplainShortageAsync(Guid userId, Guid submissionKey, CartItem line)
    {
        _db.ChangeTracker.Clear();

        // A double click can lose the stock race to its own twin. Then the order already exists.
        var existing = await FindExistingAsync(userId, submissionKey);
        if (existing is not null) return existing;

        var product = await _db.Products.AsNoTracking()
            .Where(p => p.ProductId == line.ProductId)
            .Select(p => new { p.Name, p.QuantityAvailable, p.Status })
            .FirstOrDefaultAsync();

        string message;
        if (product is null || product.Status != ProductStatus.Active)
            message = "An item in your cart is no longer available. Please review your cart.";
        else if (product.QuantityAvailable <= 0)
            message = $"{product.Name} just sold out. Please review your cart.";
        else
            message = $"Only {product.QuantityAvailable} of {product.Name} left. Lower the quantity in your cart.";

        return new CheckoutResult(CheckoutOutcome.NotEnoughStock, message);
    }

    private async Task<CheckoutResult?> FindExistingAsync(Guid userId, Guid submissionKey)
    {
        var order = await _db.Orders.AsNoTracking()
            .Where(o => o.BuyerUserId == userId && o.SubmissionKey == submissionKey)
            .Select(o => new { o.OrderId, o.OrderNumber })
            .FirstOrDefaultAsync();

        return order is null
            ? null
            : new CheckoutResult(CheckoutOutcome.Placed,
                $"Order {order.OrderNumber} was already placed.", order.OrderId, order.OrderNumber, true);
    }

    // No 0/O/1/I so numbers are easy to read out at pickup.
    private static string NewOrderNumber()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var chars = new char[6];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return $"ZB-{DateTime.UtcNow:yyyyMMdd}-{new string(chars)}";
    }
}

