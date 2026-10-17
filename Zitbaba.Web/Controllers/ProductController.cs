using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Controllers;

public class ProductController : Controller
{
    private readonly IProductDetailService _details;
    private readonly IWishlistService _wishlist;

    public ProductController(IProductDetailService details, IWishlistService wishlist)
    {
        _details = details;
        _wishlist = wishlist;
    }

    [HttpGet("Product/{id:guid}")]
    public async Task<IActionResult> Details(Guid id)
    {
        var product = await _details.GetAsync(id);
        if (product is null) return NotFound();

        if (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            ViewData["IsSaved"] = await _wishlist.IsSavedAsync(userId, id);

        return View(product);
    }
}
