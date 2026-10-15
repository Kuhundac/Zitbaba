using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Infrastructure.Security;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

public class RegistrationTests
{
    private const string StrongPassword = "Passw0rdX";

    [Fact]
    public async Task New_registration_gets_only_the_Buyer_role()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        await TestHost.EnsureRolesAsync(scope.ServiceProvider);

        var service = scope.ServiceProvider.GetRequiredService<IRegistrationService>();
        var result = await service.RegisterAsync(
            new RegistrationRequest("Test Buyer", "buyer@example.com", "0977000001", StrongPassword));

        Assert.True(result.Succeeded);

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = await users.GetRolesAsync(result.User!);
        Assert.Equal(new[] { RoleNames.Buyer }, roles.ToArray());
    }

    [Fact]
    public async Task Duplicate_email_is_rejected()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        await TestHost.EnsureRolesAsync(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<IRegistrationService>();

        var first = await service.RegisterAsync(
            new RegistrationRequest("First", "same@example.com", "0977000002", StrongPassword));
        var second = await service.RegisterAsync(
            new RegistrationRequest("Second", "SAME@example.com", "0977000003", StrongPassword));

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
    }

    [Fact]
    public async Task Duplicate_phone_is_rejected()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        await TestHost.EnsureRolesAsync(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<IRegistrationService>();

        await service.RegisterAsync(
            new RegistrationRequest("First", "a@example.com", "0977000004", StrongPassword));
        var second = await service.RegisterAsync(
            new RegistrationRequest("Second", "b@example.com", "0977000004", StrongPassword));

        Assert.False(second.Succeeded);
    }
}
