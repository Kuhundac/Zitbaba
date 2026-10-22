using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Services;

public enum CartOutcome
{
    Ok,
    NotAvailable,
    OwnProduct,
    DifferentSeller,
    NotEnoughStock,
    Invalid
}

public record CartResult(CartOutcome Outcome, string Message, string? CurrentStoreName = null)
{
    public bool Succeeded => Outcome == CartOutcome.Ok;
}

public record CartLine(
    Guid ProductId,
    string Name,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    int Available,
    string? ImageUrl,
    string? Problem);

public record CartView(Guid? SellerProfileId, string? StoreName, IReadOnlyList<CartLine> Lines)
{
    public decimal Total => Lines.Sum(l => l.LineTotal);
    public int ItemCount => Lines.Sum(l => l.Quantity);
    public bool IsEmpty => Lines.Count == 0;
    public bool HasProblems => Lines.Any(l => l.Problem != null);
}

public interface ICartService
{
    Task<CartResult> AddAsync(Guid userId, Guid productId, int quantity);
    Task<CartResult> SetQuantityAsync(Guid userId, Guid productId, int quantity);
    Task<CartResult> RemoveAsync(Guid userId, Guid productId);
    Task ClearAsync(Guid userId);
    Task<CartView> GetAsync(Guid userId);
    Task<int> CountAsync(Guid userId);
}

public class CartService : ICartService
{
    public const int MaxPerLine = 99;

    private readonly ZitbabaDbContext _db;

    public CartService(ZitbabaDbContext db) => _db = db;

    public async Task<CartResult> AddAsync(Guid userId, Guid productId, int quantity)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await TryAddAsync(userId, productId, quantity);
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                // Double click or two tabs: a unique index stopped the second insert. Try once more.
                _db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<CartResult> TryAddAsync(Guid userId, Guid productId, int quantity)
    {
        if (quantity < 1 || quantity > MaxPerLine)
            return new CartResult(CartOutcome.Invalid, $"Choose a quantity between 1 and {MaxPerLine}.");

        var product = await FindVisibleProductAsync(productId);
        if (product is null)
            return new CartResult(CartOutcome.NotAvailable, "This item is no longer available.");

        if (product.SellerUserId == userId)
            return new CartResult(CartOutcome.OwnProduct, "You cannot buy your own product.");

        if (product.QuantityAvailable < 1)
            return new CartResult(CartOutcome.NotEnoughStock, $"{product.Name} is out of stock.");

        var cart = await _db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == userId);
        if (cart is null)
        {
            cart = new Cart { UserId = userId };
            _db.Carts.Add(cart);
        }

        if (cart.Items.Count > 0 && cart.SellerProfileId != null && cart.SellerProfileId != product.SellerProfileId)
        {
            var currentStore = await _db.SellerProfiles.AsNoTracking()
                .Where(s => s.SellerProfileId == cart.SellerProfileId)
                .Select(s => s.StoreName).FirstOrDefaultAsync() ?? "another seller";
            return new CartResult(CartOutcome.DifferentSeller,
                $"Your cart already has items from {currentStore}. You can buy from one seller at a time.",
                currentStore);
        }

        var existing = cart.Items.FirstOrDefault(i => i.ProductId == productId);
        var newQuantity = (existing?.Quantity ?? 0) + quantity;

        if (newQuantity > product.QuantityAvailable)
        {
            var have = existing?.Quantity ?? 0;
            var message = have > 0
                ? $"Only {product.QuantityAvailable} of {product.Name} available, and you already have {have} in your cart."
                : $"Only {product.QuantityAvailable} of {product.Name} available.";
            return new CartResult(CartOutcome.NotEnoughStock, message);
        }

        if (existing is null)
            cart.Items.Add(new CartItem { ProductId = productId, Quantity = quantity });
        else
            existing.Quantity = newQuantity;

        cart.SellerProfileId = product.SellerProfileId;
        cart.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return new CartResult(CartOutcome.Ok, $"{product.Name} added to your cart.");
    }

    public async Task<CartResult> SetQuantityAsync(Guid userId, Guid productId, int quantity)
    {
        if (quantity <= 0) return await RemoveAsync(userId, productId);
        if (quantity > MaxPerLine)
            return new CartResult(CartOutcome.Invalid, $"Choose a quantity between 1 and {MaxPerLine}.");

        var cart = await _db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == userId);
        var item = cart?.Items.FirstOrDefault(i => i.ProductId == productId);
        if (cart is null || item is null)
            return new CartResult(CartOutcome.NotAvailable, "That item is not in your cart.");

        var product = await FindVisibleProductAsync(productId);
        if (product is null)
            return new CartResult(CartOutcome.NotAvailable, "This item is no longer available.");

        if (quantity > product.QuantityAvailable)
        {
            var message = product.QuantityAvailable == 0
                ? $"{product.Name} is out of stock."
                : $"Only {product.QuantityAvailable} of {product.Name} available.";
            return new CartResult(CartOutcome.NotEnoughStock, message);
        }

        item.Quantity = quantity;
        cart.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return new CartResult(CartOutcome.Ok, "Cart updated.");
    }

    public async Task<CartResult> RemoveAsync(Guid userId, Guid productId)
    {
        var cart = await _db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == userId);
        var item = cart?.Items.FirstOrDefault(i => i.ProductId == productId);
        if (cart is null || item is null)
            return new CartResult(CartOutcome.NotAvailable, "That item is not in your cart.");

        cart.Items.Remove(item);
        _db.CartItems.Remove(item);
        if (cart.Items.Count == 0) cart.SellerProfileId = null;
        cart.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return new CartResult(CartOutcome.Ok, "Item removed.");
    }

    public async Task ClearAsync(Guid userId)
    {
        var cart = await _db.Carts.Include(c => c.Items).FirstOrDefaultAsync(c => c.UserId == userId);
        if (cart is null) return;

        _db.CartItems.RemoveRange(cart.Items.ToList());
        cart.Items.Clear();
        cart.SellerProfileId = null;
        cart.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<CartView> GetAsync(Guid userId)
    {
        var cart = await _db.Carts.AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => new { c.CartId, c.SellerProfileId })
            .FirstOrDefaultAsync();
        if (cart is null) return new CartView(null, null, Array.Empty<CartLine>());

        var rows = await _db.CartItems.AsNoTracking()
            .Where(i => i.CartId == cart.CartId)
            .OrderBy(i => i.AddedAt).ThenBy(i => i.CartItemId)
            .Select(i => new
            {
                i.Quantity,
                i.ProductId,
                i.Product!.Name,
                i.Product.Price,
                i.Product.QuantityAvailable,
                i.Product.Status,
                Approved = i.Product.SellerProfile!.ApplicationStatus == SellerApplicationStatus.Approved,
                ImageUrl = i.Product.Images.OrderBy(x => x.SortOrder)
                    .Select(x => x.ThumbnailUrl ?? x.ImageUrl).FirstOrDefault()
            })
            .ToListAsync();

        var lines = rows.Select(r =>
        {
            string? problem = null;
            if (r.Status != ProductStatus.Active || !r.Approved)
                problem = "No longer available. Remove it to continue.";
            else if (r.QuantityAvailable < 1)
                problem = "Out of stock. Remove it to continue.";
            else if (r.Quantity > r.QuantityAvailable)
                problem = $"Only {r.QuantityAvailable} left. Lower the quantity to continue.";

            return new CartLine(r.ProductId, r.Name, r.Price, r.Quantity, r.Price * r.Quantity,
                r.QuantityAvailable, r.ImageUrl, problem);
        }).ToList();

        string? storeName = null;
        if (cart.SellerProfileId != null && lines.Count > 0)
            storeName = await _db.SellerProfiles.AsNoTracking()
                .Where(s => s.SellerProfileId == cart.SellerProfileId)
                .Select(s => s.StoreName).FirstOrDefaultAsync();

        return new CartView(lines.Count > 0 ? cart.SellerProfileId : null, storeName, lines);
    }

    public Task<int> CountAsync(Guid userId) =>
        _db.CartItems.AsNoTracking().Where(i => i.Cart!.UserId == userId).SumAsync(i => i.Quantity);

    private Task<VisibleProduct?> FindVisibleProductAsync(Guid productId) =>
        _db.Products.AsNoTracking()
            .Where(p => p.ProductId == productId
                        && p.Status == ProductStatus.Active
                        && p.SellerProfile!.ApplicationStatus == SellerApplicationStatus.Approved)
            .Select(p => new VisibleProduct(
                p.ProductId, p.Name, p.SellerProfileId, p.SellerProfile!.UserId, p.QuantityAvailable))
            .FirstOrDefaultAsync();

    private sealed record VisibleProduct(
        Guid ProductId, string Name, Guid SellerProfileId, Guid SellerUserId, int QuantityAvailable);
}

