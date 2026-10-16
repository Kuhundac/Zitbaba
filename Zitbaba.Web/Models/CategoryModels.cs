using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Zitbaba.Web.Models;

public record CategoryRow(
    int CategoryId,
    string Name,
    string? ParentName,
    bool IsActive,
    int SortOrder,
    int ProductCount,
    int ChildCount);

public record CategoryOption(int Id, string Name, bool IsActive);

public record CategoryResult(bool Succeeded, string? Error = null, int? CategoryId = null)
{
    public static CategoryResult Ok(int? id = null) => new(true, null, id);
    public static CategoryResult Fail(string error) => new(false, error, null);
}

public class CategoryFormModel
{
    public int CategoryId { get; set; }

    [Required, StringLength(100, MinimumLength = 2)]
    [Display(Name = "Category name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Parent category")]
    public int? ParentCategoryId { get; set; }

    [Range(0, 9999)]
    [Display(Name = "Sort order")]
    public int SortOrder { get; set; }

    [BindNever]
    public List<SelectListItem> Parents { get; set; } = new();
}
