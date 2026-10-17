using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Infrastructure.Security;
using Zitbaba.Web.Services;

namespace Zitbaba.Web.Infrastructure.Extensions;

public static class ServiceCollectionExtensions
{
    public const string AuthRateLimitPolicy = "auth";

    public static IServiceCollection AddZitbabaData(
        this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured. " +
                "Use appsettings.Development.json locally or the ConnectionStrings__DefaultConnection environment variable in production.");

        services.AddDbContext<ZitbabaDbContext>(o => o.UseSqlServer(connectionString));
        return services;
    }

    public static IServiceCollection AddZitbabaIdentity(
        this IServiceCollection services, IWebHostEnvironment env)
    {
        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(IdentityConfig.Configure)
            .AddEntityFrameworkStores<ZitbabaDbContext>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "Zitbaba.Auth";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            // Always Secure in production. Development allows plain http so the http profile still works.
            o.Cookie.SecurePolicy = env.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
            o.LoginPath = "/Account/Login";
            o.LogoutPath = "/Account/Logout";
            o.AccessDeniedPath = "/Account/AccessDenied";
            o.SlidingExpiration = true;
            o.ExpireTimeSpan = TimeSpan.FromDays(7);
        });

        services.AddAntiforgery(o =>
        {
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.Cookie.SecurePolicy = env.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        });

        return services;
    }

    public static IServiceCollection AddZitbabaAuthorization(this IServiceCollection services)
    {
        services.AddScoped<IAuthorizationHandler, ApprovedSellerHandler>();

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.RequireBuyer, p => p.RequireRole(RoleNames.Buyer))
            .AddPolicy(Policies.RequireAdmin, p => p.RequireRole(RoleNames.Admin))
            .AddPolicy(Policies.RequireApprovedSeller, p =>
                p.AddRequirements(new ApprovedSellerRequirement()));

        return services;
    }

    public static IServiceCollection AddZitbabaRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(AuthRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });
        return services;
    }

    public static IServiceCollection AddZitbabaServices(this IServiceCollection services)
    {
        services.AddScoped<IRegistrationService, RegistrationService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISellerApplicationService, SellerApplicationService>();
        services.AddScoped<IAccountMenuService, AccountMenuService>();
        services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        services.AddScoped<ICatalogueService, CatalogueService>();
        services.AddScoped<IProductDetailService, ProductDetailService>();
        services.AddScoped<IStorefrontService, StorefrontService>();
        services.AddScoped<IWishlistService, WishlistService>();
        services.AddScoped<ICartService, CartService>();
        services.AddScoped<ISellerApprovalQueryService, SellerApprovalQueryService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<ISellerProductService, SellerProductService>();
        services.AddSingleton<IImageStorage, LocalImageStorage>();
        services.AddScoped<IProductImageService, ProductImageService>();
        return services;
    }
}











