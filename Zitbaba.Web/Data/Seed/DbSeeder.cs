using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Infrastructure.Security;

namespace Zitbaba.Web.Data.Seed;

/// <summary>Idempotent: safe to run on every startup.</summary>
public static class DbSeeder
{
    private static readonly string[] CategoryNames =
    {
        "Fashion", "Electronics", "Food and Snacks", "Books and Stationery",
        "Beauty and Personal Care", "Home and Hostel", "Services", "Other"
    };

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        await SeedRolesAsync(sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>(), logger);
        await SeedCategoriesAsync(sp.GetRequiredService<ZitbabaDbContext>(), ct);
        await SeedAdminAsync(
            sp.GetRequiredService<UserManager<ApplicationUser>>(),
            sp.GetRequiredService<IConfiguration>(),
            logger);
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole<Guid>> roles, ILogger logger)
    {
        foreach (var name in RoleNames.All)
        {
            if (await roles.RoleExistsAsync(name)) continue;

            var result = await roles.CreateAsync(new IdentityRole<Guid>(name));
            if (!result.Succeeded)
                logger.LogError("Could not create role {Role}: {Errors}", name,
                    string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }

    private static async Task SeedCategoriesAsync(ZitbabaDbContext db, CancellationToken ct)
    {
        var existing = await db.Categories
            .Where(c => c.ParentCategoryId == null)
            .Select(c => c.Name)
            .ToListAsync(ct);

        var order = 0;
        foreach (var name in CategoryNames)
        {
            order++;
            if (existing.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            db.Categories.Add(new Category { Name = name, SortOrder = order, IsActive = true });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedAdminAsync(
        UserManager<ApplicationUser> users, IConfiguration config, ILogger logger)
    {
        var email = config["Seed:AdminEmail"];
        var password = config["Seed:AdminPassword"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Seed:AdminEmail / Seed:AdminPassword not set. Skipping admin creation.");
            return;
        }

        var admin = await users.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = config["Seed:AdminFullName"] ?? "Zitbaba Administrator",
                Phone = config["Seed:AdminPhone"] ?? "0000000000"
            };

            var created = await users.CreateAsync(admin, password);
            if (!created.Succeeded)
            {
                logger.LogError("Admin seed failed: {Errors}",
                    string.Join("; ", created.Errors.Select(e => e.Description)));
                return;
            }
            logger.LogInformation("Seeded admin user {Email}", email);
        }

        if (!await users.IsInRoleAsync(admin, RoleNames.Admin))
            await users.AddToRoleAsync(admin, RoleNames.Admin);
    }
}
