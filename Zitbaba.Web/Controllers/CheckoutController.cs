using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zitbaba.Web.Models;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Controllers;

[Authorize]
public class CheckoutController : Controller
{
    private readonly ICartService _cart;
    private readonly ICheckoutService _checkout;

    public CheckoutController(ICartService cart, ICheckoutService checkout)
    {
        _cart = cart;
        _checkout = checkout;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!TryGetUserId(out var userId)) return Challenge();

        var cart = await _cart.GetAsync(userId);
        if (cart.IsEmpty)
        {
            TempData["Error"] = "Your cart is empty.";
            return RedirectToAction("Index", "Cart");
        }
        if (cart.HasProblems)
        {
            TempData["Error"] = "Fix the items marked in your cart before you check out.";
            return RedirectToAction("Index", "Cart");
        }

        // A fresh key per page view: placing the same form twice returns the same order.
        return View(new CheckoutPageModel(cart, Guid.NewGuid()));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Place(Guid submissionKey, decimal expectedTotal)
    {
        if (!TryGetUserId(out var userId)) return Challenge();

        var result = await _checkout.PlaceOrderAsync(userId, submissionKey, expectedTotal);
        if (result.Succeeded)
        {
            TempData["Placed"] = "1";
            return RedirectToAction("Details", "Orders", new { id = result.OrderId });
        }

        TempData["Error"] = result.Message;
        return RedirectToAction("Index", "Cart");
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
