using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Models;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Controllers;

[Authorize]
public class SellerApplicationController : Controller
{
    private readonly ISellerApplicationService _applications;

    public SellerApplicationController(ISellerApplicationService applications) =>
        _applications = applications;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!TryGetUserId(out var userId)) return Challenge();

        var info = await _applications.GetStatusAsync(userId);
        if (info?.Status == SellerApplicationStatus.Approved)
            return RedirectToSellerDashboard();

        var form = info is null
            ? new SellerApplicationFormModel()
            : new SellerApplicationFormModel
            {
                StoreName = info.StoreName,
                Description = info.Description,
                ApplicationNotes = info.ApplicationNotes
            };

        return View(new SellerApplicationPageModel { Info = info, Form = form });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        [Bind(Prefix = nameof(SellerApplicationPageModel.Form))] SellerApplicationFormModel form)
    {
        if (!TryGetUserId(out var userId)) return Challenge();

        if (!ModelState.IsValid)
            return View(await PageAsync(userId, form));

        var outcome = await _applications.ApplyAsync(
            userId, new SellerApplicationRequest(form.StoreName, form.Description, form.ApplicationNotes));

        switch (outcome)
        {
            case SellerApplyOutcome.Created:
                TempData["Message"] = "Application sent. An admin will review it.";
                return RedirectToAction(nameof(Index));

            case SellerApplyOutcome.Resubmitted:
                TempData["Message"] = "Application resubmitted. An admin will review it.";
                return RedirectToAction(nameof(Index));

            case SellerApplyOutcome.AlreadyPending:
                TempData["Message"] = "You already have an application waiting for review.";
                return RedirectToAction(nameof(Index));

            case SellerApplyOutcome.AlreadyApproved:
                return RedirectToSellerDashboard();

            case SellerApplyOutcome.Suspended:
                TempData["Error"] = "Your seller account is suspended, so you cannot apply again.";
                return RedirectToAction(nameof(Index));

            case SellerApplyOutcome.StoreNameTaken:
                ModelState.AddModelError(
                    $"{nameof(SellerApplicationPageModel.Form)}.{nameof(form.StoreName)}",
                    "That store name is already taken. Please choose another.");
                return View(await PageAsync(userId, form));

            default:
                ModelState.AddModelError(string.Empty, "Please check the form and try again.");
                return View(await PageAsync(userId, form));
        }
    }

    private async Task<SellerApplicationPageModel> PageAsync(Guid userId, SellerApplicationFormModel form) =>
        new() { Info = await _applications.GetStatusAsync(userId), Form = form };

    private IActionResult RedirectToSellerDashboard() =>
        RedirectToAction("Index", "Home", new { area = "Seller" });

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
}