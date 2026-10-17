using Microsoft.AspNetCore.Mvc;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Controllers;

public class ProductController : Controller
{
    private readonly IProductDetailService _details;

    public ProductController(IProductDetailService details) => _details = details;

    [HttpGet("Product/{id:guid}")]
    public async Task<IActionResult> Details(Guid id)
    {
        var product = await _details.GetAsync(id);
        return product is null ? NotFound() : View(product);
    }
}
