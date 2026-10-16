using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Zitbaba.Web.Infrastructure.Security;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.ViewComponents;

public class AccountMenuViewComponent : ViewComponent
{
    private readonly IAccountMenuService _menu;

    public AccountMenuViewComponent(IAccountMenuService menu) => _menu = menu;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var user = HttpContext.User;
        Guid? id = null;
        if (user.Identity?.IsAuthenticated == true
            && Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var parsed))
            id = parsed;

        var state = await _menu.GetAsync(id, user.IsInRole(RoleNames.Admin));
        return View(state);
    }
}
