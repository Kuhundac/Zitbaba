using Microsoft.AspNetCore.Identity;
using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Domain.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
    }

    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }
    public string? Programme { get; set; }
    public byte? YearOfStudy { get; set; }
    public string? ResidenceOrRoom { get; set; }

    public VisibilityLevel PhoneVisibility { get; set; } = VisibilityLevel.Private;
    public VisibilityLevel ResidenceVisibility { get; set; } = VisibilityLevel.Private;
    public AccountStatus AccountStatus { get; set; } = AccountStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public SellerProfile? SellerProfile { get; set; }
}
