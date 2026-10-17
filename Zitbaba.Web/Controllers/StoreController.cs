using Microsoft.AspNetCore.Mvc;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Controllers;

public class StoreController : Controller
{
    private readonly IStorefrontService _storefront;

    public StoreController(IStorefrontService storefront) => _storefront = storefront;

    [HttpGet("Store/{id:guid}")]
    public async Task<IActionResult> Index(Guid id, int page = 1)
    {
        var model = await _storefront.GetAsync(id, page);
        return model is null ? NotFound() : View(model);
    }
}
