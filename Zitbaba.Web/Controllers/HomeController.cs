using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Zitbaba.Web.Models;

namespace Zitbaba.Web.Controllers;

public class HomeController : Controller
{
    public IActionResult Index() => View();

    // Placeholders so the navbar links work on Day 1. Real pages arrive on Day 3.
    public IActionResult Categories() => ComingSoon("Categories");
    public IActionResult Search(string? q) => ComingSoon("Search");
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
