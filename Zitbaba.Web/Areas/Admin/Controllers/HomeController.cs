using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zitbaba.Web.Infrastructure.Security;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = Policies.RequireAdmin)]
public class HomeController : Controller
{
    private readonly IAdminDashboardService _dashboard;

    public HomeController(IAdminDashboardService dashboard) => _dashboard = dashboard;

    public async Task<IActionResult> Index() => View(await _dashboard.GetAsync());
}
