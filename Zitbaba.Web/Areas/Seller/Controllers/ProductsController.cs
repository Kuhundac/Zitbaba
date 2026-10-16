using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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
    private const int MaxRequestBytes = 8 * 1024 * 1024;

    private readonly ISellerProductService _products;
    private readonly IProductImageService _images;

    public ProductsController(ISellerProductService products, IProductImageService images)
    {
        _products = products;
        _images = images;
    }

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

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(MaxRequestBytes)]
    public async Task<IActionResult> Create(SellerProductFormModel model)
    {
        var upload = await ReadUploadAsync(model.Image);
        await CheckImageAsync(upload);

        if (ModelState.IsValid)
        {
            var userId = User.GetUserId();
            var result = await _products.CreateAsync(userId, ToInput(model));
            if (result.Succeeded)
            {
                if (upload is not null)
                {
                    var image = await _images.SetImageAsync(userId, result.ProductId!.Value, upload);
                    if (!image.Succeeded)
                    {
                        TempData["ProductError"] = "The product was created, but the image could not be saved: " + image.Error;
                        return RedirectToAction(nameof(Edit), new { id = result.ProductId });
                    }
                }

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

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(MaxRequestBytes)]
    public async Task<IActionResult> Edit(SellerProductFormModel model)
    {
        var upload = await ReadUploadAsync(model.Image);
        await CheckImageAsync(upload);

        if (ModelState.IsValid)
        {
            var userId = User.GetUserId();
            var result = await _products.UpdateAsync(userId, model.ProductId, ToInput(model), model.RowVersion);
            if (result.Outcome == ProductOutcome.NotFound) return NotFound();

            if (result.Succeeded)
            {
                ProductResult? image = null;
                if (upload is not null)
                    image = await _images.SetImageAsync(userId, model.ProductId, upload);
                else if (model.RemoveImage)
                    image = await _images.RemoveImageAsync(userId, model.ProductId);

                if (image is { Succeeded: false })
                {
                    TempData["ProductError"] = "The product was saved, but the image could not be updated: " + image.Error;
                    return RedirectToAction(nameof(Edit), new { id = model.ProductId });
                }

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

    private async Task CheckImageAsync(ImageUpload? upload)
    {
        if (upload is null) return;
        var check = await _images.ValidateAsync(upload);
        if (!check.Ok)
            ModelState.AddModelError(nameof(SellerProductFormModel.Image), check.Error!);
    }

    private static async Task<ImageUpload?> ReadUploadAsync(IFormFile? file)
    {
        // A file input left empty arrives as null (or an unnamed, zero-length part).
        if (file is null || (file.Length == 0 && string.IsNullOrEmpty(file.FileName))) return null;

        var buffer = new MemoryStream();
        await file.CopyToAsync(buffer);
        buffer.Position = 0;
        return new ImageUpload(file.FileName, buffer.Length, buffer);
    }

    private async Task FillOptionsAsync(SellerProductFormModel model)
    {
        var options = await _products.GetCategoryOptionsAsync(model.CategoryId == 0 ? null : model.CategoryId);
        model.Categories = options
            .Select(o => new SelectListItem(o.IsActive ? o.Name : o.Name + " (inactive)", o.Id.ToString()))
            .ToList();

        if (model.ProductId != Guid.Empty)
            model.ExistingImageUrl = await _images.GetImageUrlAsync(User.GetUserId(), model.ProductId);
    }

    private static SellerProductInput ToInput(SellerProductFormModel m) =>
        new(m.Name, m.CategoryId, m.Description, m.Price, m.QuantityAvailable, m.Condition, m.LowStockThreshold);
}
