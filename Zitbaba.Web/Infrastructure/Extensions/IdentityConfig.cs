using Microsoft.AspNetCore.Identity;

namespace Zitbaba.Web.Infrastructure.Extensions;

/// <summary>Single source of truth for Identity options (used by the app and by tests).</summary>
public static class IdentityConfig
{
    public static void Configure(IdentityOptions o)
    {
        o.User.RequireUniqueEmail = true;

        o.Password.RequiredLength = 8;
        o.Password.RequireUppercase = true;
        o.Password.RequireLowercase = true;
        o.Password.RequireDigit = true;
        o.Password.RequireNonAlphanumeric = false;

        o.Lockout.AllowedForNewUsers = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    }
}
