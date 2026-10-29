using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zitbaba.Web.Infrastructure.Security;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = Policies.RequireAdmin)]
public class AuditLogController : Controller
{
    private readonly IAuditLogQueryService _audit;

    public AuditLogController(IAuditLogQueryService audit) => _audit = audit;

    // The query key is "actionName" because a parameter called "action" would collide with MVC's route value.
    [HttpGet]
    public async Task<IActionResult> Index(
        string? actionName, string? entityType, string? entityId, string? actor,
        DateTime? fromDate, DateTime? toDate, int page = 1)
    {
        var filter = new AuditLogFilter(actionName, entityType, entityId, actor, fromDate, toDate, page);
        return View(await _audit.GetPageAsync(filter));
    }
}
