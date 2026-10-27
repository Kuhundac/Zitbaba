using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zitbaba.Web.Infrastructure.Security;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = Policies.RequireAdmin)]
public class UsersController : Controller
{
    private readonly IAdminUserService _users;

    public UsersController(IAdminUserService users) => _users = users;

    [HttpGet]
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        var result = await _users.ListAsync(q, page);
        return View(new AdminUsersIndexModel(q, result));
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        var details = await _users.GetAsync(id);
        return details is null ? NotFound() : View(details);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Suspend(Guid id, string? reason)
    {
        if (!TryGetAdminId(out var adminId)) return Challenge();
        var outcome = await _users.SuspendAsync(id, adminId, reason);
        return Finish(id, outcome, "Account suspended. The person is signed out within about a minute.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivate(Guid id)
    {
        if (!TryGetAdminId(out var adminId)) return Challenge();
        var outcome = await _users.ReactivateAsync(id, adminId);
        return Finish(id, outcome, "Account reactivated. A suspended seller account stays suspended until you reinstate it.");
    }

    private IActionResult Finish(Guid id, AccountActionOutcome outcome, string doneMessage)
    {
        if (outcome == AccountActionOutcome.Done)
            TempData["Message"] = doneMessage;
        else
            TempData["Error"] = outcome switch
            {
                AccountActionOutcome.NotFound => "User not found.",
                AccountActionOutcome.InvalidState => "That action is not available for the current status of this account.",
                AccountActionOutcome.ReasonRequired => "Enter a reason of 5 to 500 characters.",
                AccountActionOutcome.CannotActOnSelf => "You cannot suspend your own account.",
                AccountActionOutcome.CannotSuspendAdmin => "Administrator accounts cannot be suspended here.",
                _ => "Something went wrong. Nothing was changed."
            };

        return RedirectToAction("Details", "Users", new { area = "Admin", id });
    }

    private bool TryGetAdminId(out Guid adminId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out adminId);
}
