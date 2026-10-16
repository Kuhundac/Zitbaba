using System.ComponentModel.DataAnnotations;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Models;

public class SellerApplicationFormModel
{
    [Required(ErrorMessage = "Enter a store name.")]
    [StringLength(SellerApplicationLimits.StoreNameMax,
        ErrorMessage = "Store name can be at most {1} characters.")]
    [Display(Name = "Store name")]
    public string StoreName { get; set; } = string.Empty;

    [StringLength(SellerApplicationLimits.TextMax,
        ErrorMessage = "Description can be at most {1} characters.")]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [StringLength(SellerApplicationLimits.TextMax,
        ErrorMessage = "This can be at most {1} characters.")]
    [Display(Name = "What do you plan to sell?")]
    public string? ApplicationNotes { get; set; }
}

public class SellerApplicationPageModel
{
    public SellerApplicationInfo? Info { get; set; }
    public SellerApplicationFormModel Form { get; set; } = new();
}