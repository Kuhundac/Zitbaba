using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

/// <summary>Guards against "Unable to resolve service" errors that only appear in the running site,
/// because the tests build their own container. This uses the app's real registration method.</summary>
public class ServiceRegistrationTests
{
    [Fact]
    public void Every_app_service_that_controllers_and_services_need_is_registered()
    {
        var webAssembly = typeof(ICartService).Assembly;
        var services = new ServiceCollection();
        RegisterAppServices(services, webAssembly);

        var consumers = new List<Type>();
        consumers.AddRange(webAssembly.GetTypes().Where(t =>
            !t.IsAbstract
            && (typeof(ControllerBase).IsAssignableFrom(t) || typeof(ViewComponent).IsAssignableFrom(t))));
        consumers.AddRange(services
            .Where(d => d.ImplementationType is not null && d.ImplementationType.Assembly == webAssembly)
            .Select(d => d.ImplementationType!));

        var missing = new SortedSet<string>();
        foreach (var consumer in consumers.Distinct())
        {
            var constructor = consumer.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault();
            if (constructor is null) continue;

            foreach (var parameter in constructor.GetParameters())
            {
                var type = parameter.ParameterType;
                if (type.Assembly != webAssembly || !type.IsInterface) continue;
                if (!services.Any(d => d.ServiceType == type))
                    missing.Add($"{consumer.Name} needs {type.Name}");
            }
        }

        Assert.True(missing.Count == 0,
            "Not registered in AddZitbabaServices: " + string.Join("; ", missing));
    }

    private static void RegisterAppServices(IServiceCollection services, Assembly webAssembly)
    {
        var host = webAssembly.GetTypes().Single(t =>
            t.IsAbstract && t.IsSealed && t.GetMethod("AddZitbabaServices") is not null);
        var method = host.GetMethod("AddZitbabaServices")!;

        var arguments = method.GetParameters().Select(p =>
        {
            if (p.ParameterType == typeof(IServiceCollection)) return (object)services;
            if (p.ParameterType == typeof(IConfiguration)) return (object)new ConfigurationBuilder().Build();
            throw new InvalidOperationException(
                $"The test cannot supply a {p.ParameterType.Name} to AddZitbabaServices. Paste me the method signature.");
        }).ToArray();

        method.Invoke(null, arguments);
    }
}
