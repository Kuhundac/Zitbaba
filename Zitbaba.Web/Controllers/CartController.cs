using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Controllers;

[Authorize]
public class CartController : Controller
{
    private readonly ICartService _cart;

    public CartController(ICartService cart) => _cart = cart;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!TryGetUserId(out var userId)) return Challenge();
        return View(await _cart.GetAsync(userId));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(Guid productId, int quantity = 1, string? returnUrl = null)
    {
        if (!TryGetUserId(out var userId)) return Challenge();

        var result = await _cart.AddAsync(userId, productId, quantity);
        if (result.Succeeded)
        {
            TempData["Message"] = result.Message;
        }
        else
        {
            TempData["Error"] = result.Message;
            if (result.Outcome == CartOutcome.DifferentSeller)
            {
                // Lets the product page offer "clear cart and add this item".
                TempData["CartConflictProductId"] = productId.ToString();
                TempData["CartConflictQuantity"] = quantity.ToString();
            }
        }

        return Back(returnUrl);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ReplaceAndAdd(Guid productId, int quantity = 1, string? returnUrl = null)
    {
        if (!TryGetUserId(out var userId)) return Challenge();

        await _cart.ClearAsync(userId);
        var result = await _cart.AddAsync(userId, productId, quantity);

        if (result.Succeeded)
            TempData["Message"] = "Your cart was cleared and the new item was added.";
        else
            TempData["Error"] = "Your cart was cleared, but the new item could not be added: " + result.Message;

        return Back(returnUrl);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SetQuantity(Guid productId, int quantity)
    {
        if (!TryGetUserId(out var userId)) return Challenge();

        var result = await _cart.SetQuantityAsync(userId, productId, quantity);
        TempData[result.Succeeded ? "Message" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(Guid productId)
    {
        if (!TryGetUserId(out var userId)) return Challenge();

        var result = await _cart.RemoveAsync(userId, productId);
        TempData[result.Succeeded ? "Message" : "Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Clear()
    {
        if (!TryGetUserId(out var userId)) return Challenge();

        await _cart.ClearAsync(userId);
        TempData["Message"] = "Your cart is empty.";
        return RedirectToAction(nameof(Index));
    }

    private IActionResult Back(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction(nameof(Index));

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
