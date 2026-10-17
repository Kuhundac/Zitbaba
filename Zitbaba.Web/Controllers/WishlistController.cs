using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Controllers;

[Authorize]
public class WishlistController : Controller
{
    private readonly IWishlistService _wishlist;

    public WishlistController(IWishlistService wishlist) => _wishlist = wishlist;

    [HttpGet]
    public async Task<IActionResult> Index(int page = 1)
    {
        if (!TryGetUserId(out var userId)) return Challenge();
        return View(await _wishlist.ListAsync(userId, page));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Add(Guid productId, string? returnUrl)
    {
        if (!TryGetUserId(out var userId)) return Challenge();

        var outcome = await _wishlist.AddAsync(userId, productId);
        if (outcome == WishlistOutcome.NotAvailable)
            TempData["Error"] = "That product is no longer available.";
        else
            TempData["Message"] = outcome == WishlistOutcome.Added
                ? "Saved to your wishlist."
                : "It is already in your wishlist.";

        return Back(returnUrl);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(Guid productId, string? returnUrl)
    {
        if (!TryGetUserId(out var userId)) return Challenge();

        await _wishlist.RemoveAsync(userId, productId);
        TempData["Message"] = "Removed from your wishlist.";
        return Back(returnUrl);
    }

    private IActionResult Back(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction(nameof(Index));

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
