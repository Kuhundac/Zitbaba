using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Tests;

/// <summary>Runs only when ZITBABA_TEST_SQL holds a SQL Server connection string (database Zitbaba_Test).
/// Otherwise the test is reported as skipped, so a plain dotnet test and CI stay green.</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerTestDb.EnvVar)))
            Skip = "Set ZITBABA_TEST_SQL to a SQL Server connection string (database Zitbaba_Test) to run this test.";
    }
}

public sealed record SeededSeller(Guid SellerUserId, Guid SellerProfileId, int CategoryId);

internal static class SqlServerTestDb
{
    public const string EnvVar = "ZITBABA_TEST_SQL";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _ready;

    /// <summary>A fresh DbContext on the test database. The first call applies the real migrations.</summary>
    public static async Task<ZitbabaDbContext> NewContextAsync()
    {
        var cs = Environment.GetEnvironmentVariable(EnvVar)
                 ?? throw new InvalidOperationException(EnvVar + " is not set.");
        var database = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(cs).InitialCatalog;
        if (!string.Equals(database, "Zitbaba_Test", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(EnvVar + " must use the database Zitbaba_Test. Refusing to run against database " + database + ".");
        var options = new DbContextOptionsBuilder<ZitbabaDbContext>().UseSqlServer(cs).Options;

        if (!_ready)
        {
            await Gate.WaitAsync();
            try
            {
                if (!_ready)
                {
                    await using var migrating = new ZitbabaDbContext(options);
                    await migrating.Database.MigrateAsync();
                    _ready = true;
                }
            }
            finally
            {
                Gate.Release();
            }
        }

        return new ZitbabaDbContext(options);
    }

    private static string Tag() => Guid.NewGuid().ToString("N");

    public static async Task<Guid> SeedUserAsync(ZitbabaDbContext db)
    {
        var tag = Tag();
        var user = new ApplicationUser
        {
            UserName = "t" + tag,
            NormalizedUserName = ("T" + tag).ToUpperInvariant(),
            Email = $"t{tag}@test.local",
            NormalizedEmail = $"T{tag}@TEST.LOCAL".ToUpperInvariant(),
            FullName = "Test User",
            Phone = tag[..12],
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    public static async Task<SeededSeller> SeedSellerAsync(ZitbabaDbContext db)
    {
        var tag = Tag();
        var userId = await SeedUserAsync(db);

        var category = new Category { Name = "Cat " + tag[..10] };
        db.Categories.Add(category);

        var seller = new SellerProfile
        {
            UserId = userId,
            StoreName = "Store " + tag[..10],
            ApplicationStatus = SellerApplicationStatus.Approved
        };
        db.SellerProfiles.Add(seller);
        await db.SaveChangesAsync();

        return new SeededSeller(userId, seller.SellerProfileId, category.CategoryId);
    }

    public static async Task<Product> SeedProductAsync(
        ZitbabaDbContext db, SeededSeller seller, decimal price, int quantity, string name)
    {
        var product = new Product
        {
            SellerProfileId = seller.SellerProfileId,
            CategoryId = seller.CategoryId,
            Name = name,
            Price = price,
            QuantityAvailable = quantity,
            Status = ProductStatus.Active
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return product;
    }
}

