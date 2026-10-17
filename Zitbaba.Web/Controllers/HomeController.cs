using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Models;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Controllers;

public class HomeController : Controller
{
    private const int RecentCount = 8;

    private readonly ICatalogueService _catalogue;

    public HomeController(ICatalogueService catalogue) => _catalogue = catalogue;

    public async Task<IActionResult> Index()
    {
        var recent = await _catalogue.GetRecentAsync(RecentCount);
        var categories = await _catalogue.GetCategoriesAsync();
        return View(new HomeIndexModel(recent, categories));
    }

    public async Task<IActionResult> Categories() =>
        View(await _catalogue.GetCategoriesAsync());

    // The service ignores or corrects bad values (negative prices, unknown enum numbers, page out of range).
    public async Task<IActionResult> Search(
        string? q, int? categoryId, decimal? minPrice, decimal? maxPrice,
        ProductCondition? condition, bool inStock = false,
        CatalogueSort sort = CatalogueSort.Newest, int page = 1)
    {
        var query = new CatalogueQuery(
            Keyword: q,
            CategoryId: categoryId,
            MinPrice: minPrice,
            MaxPrice: maxPrice,
            Condition: condition,
            InStockOnly: inStock,
            Sort: sort,
            Page: page);

        var result = await _catalogue.SearchAsync(query);
        var categories = await _catalogue.GetCategoriesAsync();
        return View(new SearchPageModel(query, result, categories));
    }

    // Placeholder until Day 3 step 6.
    public IActionResult Wishlist() => ComingSoon("Wishlist");

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });

    private IActionResult ComingSoon(string title)
    {
        ViewData["Title"] = title;
        return View("ComingSoon");
    }
}
