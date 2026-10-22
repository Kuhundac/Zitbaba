using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Infrastructure.Extensions;
using Zitbaba.Web.Infrastructure.Security;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

/// <summary>Builds a small DI container backed by an in-memory database for each test.</summary>
internal sealed class TestHost : IDisposable
{
    private readonly ServiceProvider _provider;

    public TestHost(IDictionary<string, string?>? settings = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build());
        services.AddDbContext<ZitbabaDbContext>(o =>
            o.UseInMemoryDatabase("zitbaba-" + Guid.NewGuid())
             // The in-memory provider has no real transactions; ignore the warning so services can use them.
             .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services
            .AddIdentityCore<ApplicationUser>(IdentityConfig.Configure)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ZitbabaDbContext>();
        services.AddScoped<IRegistrationService, RegistrationService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ISellerApplicationService, SellerApplicationService>();
        services.AddScoped<ISellerProductService, SellerProductService>();
        services.AddSingleton<IImageStorage, InMemoryImageStorage>();
        services.AddScoped<IProductImageService, ProductImageService>();
        services.AddScoped<IAccountMenuService, AccountMenuService>();
        services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<ICheckoutService, CheckoutService>();
        _provider = services.BuildServiceProvider();
    }

    public IServiceScope CreateScope() => _provider.CreateScope();

    public static async Task EnsureRolesAsync(IServiceProvider sp)
    {
        var roles = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var name in RoleNames.All)
            if (!await roles.RoleExistsAsync(name))
                await roles.CreateAsync(new IdentityRole<Guid>(name));
    }

    public void Dispose() => _provider.Dispose();
}




