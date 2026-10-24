using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zitbaba.Web.Infrastructure.Extensions;
using Zitbaba.Web.Infrastructure.Security;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Areas.Seller.Controllers;

[Area("Seller")]
[Authorize(Policy = Policies.RequireApprovedSeller)]
public class HomeController : Controller
{
    private readonly ISellerProductService _products;
    private readonly ISellerOrderService _orders;

    public HomeController(ISellerProductService products, ISellerOrderService orders)
    {
        _products = products;
        _orders = orders;
    }

    public async Task<IActionResult> Index()
    {
        var userId = User.GetUserId();
        ViewData["PendingOrders"] = await _orders.CountPendingAsync(userId);
        return View(await _products.GetDashboardAsync(userId));
    }
}
