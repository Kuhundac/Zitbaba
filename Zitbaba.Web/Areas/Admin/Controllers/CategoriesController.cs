using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Zitbaba.Web.Infrastructure.Security;
using Zitbaba.Web.Models;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = Policies.RequireAdmin)]
public class CategoriesController : Controller
{
    private readonly ICategoryService _categories;

    public CategoriesController(ICategoryService categories) => _categories = categories;

    private Guid ActorId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> Index() => View(await _categories.ListAsync());

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var model = new CategoryFormModel();
        await FillParentsAsync(model);
        return View("Form", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CategoryFormModel model)
    {
        if (ModelState.IsValid)
        {
            var result = await _categories.CreateAsync(ActorId, model.Name, model.ParentCategoryId, model.SortOrder);
            if (result.Succeeded)
            {
                TempData["CategoryMessage"] = $"Category '{model.Name.Trim()}' created.";
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }

        await FillParentsAsync(model);
        return View("Form", model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var model = await _categories.GetFormAsync(id);
        if (model is null) return NotFound();
        await FillParentsAsync(model);
        return View("Form", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CategoryFormModel model)
    {
        if (ModelState.IsValid)
        {
            var result = await _categories.UpdateAsync(ActorId, model.CategoryId, model.Name, model.ParentCategoryId, model.SortOrder);
            if (result.Succeeded)
            {
                TempData["CategoryMessage"] = $"Category '{model.Name.Trim()}' saved.";
                return RedirectToAction(nameof(Index));
            }
            ModelState.AddModelError(string.Empty, result.Error!);
        }

        await FillParentsAsync(model);
        return View("Form", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id, bool active)
    {
        var result = await _categories.SetActiveAsync(ActorId, id, active);
        if (result.Succeeded)
            TempData["CategoryMessage"] = active ? "Category activated." : "Category deactivated.";
        else
            TempData["CategoryError"] = result.Error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _categories.DeleteAsync(ActorId, id);
        if (result.Succeeded)
            TempData["CategoryMessage"] = "Category deleted.";
        else
            TempData["CategoryError"] = result.Error;
        return RedirectToAction(nameof(Index));
    }

    private async Task FillParentsAsync(CategoryFormModel model)
    {
        var options = await _categories.GetParentOptionsAsync(model.CategoryId == 0 ? null : model.CategoryId);
        model.Parents = options
            .Select(o => new SelectListItem(o.IsActive ? o.Name : o.Name + " (inactive)", o.Id.ToString()))
            .ToList();
    }
}
