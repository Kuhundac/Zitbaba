using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Controllers;

[Authorize]
public class OrdersController : Controller
{
    private readonly IBuyerOrderService _orders;

    public OrdersController(IBuyerOrderService orders) => _orders = orders;

    [HttpGet]
    public async Task<IActionResult> Index(int page = 1)
    {
        if (!TryGetUserId(out var userId)) return Challenge();
        return View(await _orders.ListAsync(userId, page));
    }

    [HttpGet("Orders/{id:guid}")]
    public async Task<IActionResult> Details(Guid id)
    {
        if (!TryGetUserId(out var userId)) return Challenge();

        var order = await _orders.GetAsync(userId, id);
        if (order is null) return NotFound();
        return View(order);
    }

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}
