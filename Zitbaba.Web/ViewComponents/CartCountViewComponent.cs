using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.ViewComponents;

/// <summary>The Cart link in the main menu, with an item count. Signed-in users only.</summary>
public class CartCountViewComponent : ViewComponent
{
    private readonly ICartService _cart;

    public CartCountViewComponent(ICartService cart) => _cart = cart;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var user = HttpContext.User;
        if (user.Identity?.IsAuthenticated != true
            || !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Content(string.Empty);

        var count = await _cart.CountAsync(userId);
        var href = Url.Action("Index", "Cart", new { area = "" }) ?? "/Cart";
        var label = count > 0 ? $"Cart ({count})" : "Cart";
        var encoder = HtmlEncoder.Default;

        var html = $"<a class=\"nav-link\" href=\"{encoder.Encode(href)}\">{encoder.Encode(label)}</a>";
        return new HtmlContentViewComponentResult(new HtmlString(html));
    }
}
