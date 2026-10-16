using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Infrastructure.Security;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = Policies.RequireAdmin)]
public class SellerApprovalsController : Controller
{
    private const int PageSize = 20;

    private readonly ISellerApprovalQueryService _queries;
    private readonly ISellerApplicationService _applications;

    public SellerApprovalsController(
        ISellerApprovalQueryService queries, ISellerApplicationService applications)
    {
        _queries = queries;
        _applications = applications;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? status, int page = 1)
    {
        var filter = NormaliseFilter(status);
        SellerApplicationStatus? parsed = filter == "All"
            ? null
            : Enum.Parse<SellerApplicationStatus>(filter);

        var result = await _queries.ListAsync(parsed, page, PageSize);
        return View(new SellerApprovalsIndexModel(filter, result));
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        var details = await _queries.GetDetailsAsync(id);
        return details is null ? NotFound() : View(details);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(Guid id)
    {
        if (!TryGetAdminId(out var adminId)) return Challenge();
        var outcome = await _applications.ApproveAsync(id, adminId);
        return Finish(id, outcome, "Seller approved. They can now use the seller dashboard.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(Guid id, string? reason)
    {
        if (!TryGetAdminId(out var adminId)) return Challenge();
        var outcome = await _applications.RejectAsync(id, adminId, reason);
        return Finish(id, outcome, "Application rejected. The applicant will see your reason.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Suspend(Guid id, string? reason)
    {
        if (!TryGetAdminId(out var adminId)) return Challenge();
        var outcome = await _applications.SuspendAsync(id, adminId, reason);
        return Finish(id, outcome, "Seller suspended. They can no longer publish products.");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reinstate(Guid id)
    {
        if (!TryGetAdminId(out var adminId)) return Challenge();
        var outcome = await _applications.ReinstateAsync(id, adminId);
        return Finish(id, outcome, "Seller reinstated.");
    }

    private IActionResult Finish(Guid id, SellerReviewOutcome outcome, string doneMessage)
    {
        if (outcome == SellerReviewOutcome.Done)
            TempData["Message"] = doneMessage;
        else
            TempData["Error"] = outcome switch
            {
                SellerReviewOutcome.NotFound => "Application not found.",
                SellerReviewOutcome.InvalidState => "That action is not available for the current status. Check the status and try again.",
                SellerReviewOutcome.ReasonRequired => "Enter a reason of 5 to 500 characters.",
                SellerReviewOutcome.RoleFailed => "The Seller role could not be granted. Nothing was changed.",
                _ => "Something went wrong. Nothing was changed."
            };

        return RedirectToAction(nameof(Details), new { id });
    }

    private static string NormaliseFilter(string? status)
    {
        if (string.Equals(status, "All", StringComparison.OrdinalIgnoreCase)) return "All";
        if (Enum.TryParse<SellerApplicationStatus>(status, true, out var parsed) && Enum.IsDefined(parsed))
            return parsed.ToString();
        return nameof(SellerApplicationStatus.Pending);
    }

    private bool TryGetAdminId(out Guid adminId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out adminId);
}
