using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Zitbaba.Web.Infrastructure.Extensions;
using Zitbaba.Web.Infrastructure.Security;
using Zitbaba.Web.Models;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Areas.Seller.Controllers;

[Area("Seller")]
[Authorize(Policy = Policies.RequireApprovedSeller)]
public class ProductsController : Controller
{
    private readonly ISellerProductService _products;

    public ProductsController(ISellerProductService products) => _products = products;

    [HttpGet]
    public async Task<IActionResult> Index(int page = 1) =>
        View(await _products.ListAsync(User.GetUserId(), page));

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = new SellerProductFormModel();
        await FillOptionsAsync(model);
        return View("Form", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SellerProductFormModel model)
    {
        if (ModelState.IsValid)
        {
            var result = await _products.CreateAsync(User.GetUserId(), ToInput(model));
            if (result.Succeeded)
            {
                TempData["ProductMessage"] = $"Product '{model.Name.Trim()}' created.";
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not save the product.");
        }

        await FillOptionsAsync(model);
        return View("Form", model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var model = await _products.GetFormAsync(User.GetUserId(), id);
        if (model is null) return NotFound();
        await FillOptionsAsync(model);
        return View("Form", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(SellerProductFormModel model)
    {
        if (ModelState.IsValid)
        {
            var result = await _products.UpdateAsync(User.GetUserId(), model.ProductId, ToInput(model), model.RowVersion);
            if (result.Outcome == ProductOutcome.NotFound) return NotFound();
            if (result.Succeeded)
            {
                TempData["ProductMessage"] = $"Product '{model.Name.Trim()}' saved.";
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError(string.Empty, result.Error ?? "Could not save the product.");
        }

        await FillOptionsAsync(model);
        return View("Form", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Deactivate(Guid id) => ChangeStatusAsync(id, ProductAction.Deactivate, "Product deactivated.");

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Reactivate(Guid id) => ChangeStatusAsync(id, ProductAction.Reactivate, "Product reactivated.");

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Remove(Guid id) => ChangeStatusAsync(id, ProductAction.Remove, "Product removed.");

    private async Task<IActionResult> ChangeStatusAsync(Guid id, ProductAction action, string successMessage)
    {
        var result = await _products.SetStatusAsync(User.GetUserId(), id, action);
        if (result.Outcome == ProductOutcome.NotFound) return NotFound();

        if (result.Succeeded) TempData["ProductMessage"] = successMessage;
        else TempData["ProductError"] = result.Error;
        return RedirectToAction(nameof(Index));
    }

    private async Task FillOptionsAsync(SellerProductFormModel model)
    {
        var options = await _products.GetCategoryOptionsAsync(model.CategoryId == 0 ? null : model.CategoryId);
        model.Categories = options
            .Select(o => new SelectListItem(o.IsActive ? o.Name : o.Name + " (inactive)", o.Id.ToString()))
            .ToList();
    }

    private static SellerProductInput ToInput(SellerProductFormModel m) =>
        new(m.Name, m.CategoryId, m.Description, m.Price, m.QuantityAvailable, m.Condition, m.LowStockThreshold);
}
