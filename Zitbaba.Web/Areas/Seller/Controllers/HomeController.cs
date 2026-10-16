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

    public HomeController(ISellerProductService products) => _products = products;

    public async Task<IActionResult> Index() =>
        View(await _products.GetDashboardAsync(User.GetUserId()));
}
