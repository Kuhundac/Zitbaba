using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
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

    public TestHost()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ZitbabaDbContext>(o =>
            o.UseInMemoryDatabase("zitbaba-" + Guid.NewGuid()));
        services
            .AddIdentityCore<ApplicationUser>(IdentityConfig.Configure)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ZitbabaDbContext>();
        services.AddScoped<IRegistrationService, RegistrationService>();
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
