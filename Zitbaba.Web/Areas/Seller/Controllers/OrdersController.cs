using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zitbaba.Web.Infrastructure.Extensions;
using Zitbaba.Web.Infrastructure.Security;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Areas.Seller.Controllers;

[Area("Seller")]
[Authorize(Policy = Policies.RequireApprovedSeller)]
public class OrdersController : Controller
{
    private readonly ISellerOrderService _orders;
    private readonly IOrderTransitionService _transitions;

    public OrdersController(ISellerOrderService orders, IOrderTransitionService transitions)
    {
        _orders = orders;
        _transitions = transitions;
    }

    [HttpGet]
    public async Task<IActionResult> Index(SellerOrderFilter filter = SellerOrderFilter.Pending, int page = 1)
    {
        ViewData["Filter"] = filter;
        return View(await _orders.ListAsync(User.GetUserId(), filter, page));
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        var order = await _orders.GetAsync(User.GetUserId(), id);
        if (order is null) return NotFound();
        return View(order);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Accept(Guid id) =>
        ActAsync(id, userId => _transitions.SellerAcceptAsync(userId, id));

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Reject(Guid id, string? reason) =>
        ActAsync(id, userId => _transitions.SellerRejectAsync(userId, id, reason));

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Ready(Guid id) =>
        ActAsync(id, userId => _transitions.SellerMarkReadyAsync(userId, id));

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Complete(Guid id) =>
        ActAsync(id, userId => _transitions.SellerCompleteAsync(userId, id));

    private async Task<IActionResult> ActAsync(Guid id, Func<Guid, Task<TransitionResult>> action)
    {
        var result = await action(User.GetUserId());
        TempData[result.Succeeded ? "OrderMessage" : "OrderError"] = result.Message;
        return RedirectToAction(nameof(Details), new { id });
    }
}
