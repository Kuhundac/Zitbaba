using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Zitbaba.Web.Data;
using Zitbaba.Web.Data.Seed;
using Zitbaba.Web.Infrastructure.Extensions;
using Zitbaba.Web.Infrastructure.Security;

System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) => logger
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddControllersWithViews(o =>
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

builder.Services
    .AddZitbabaData(builder.Configuration)
    .AddZitbabaIdentity(builder.Environment)
    .AddZitbabaAuthorization()
    .AddZitbabaRateLimiting()
    .AddZitbabaServices();

builder.Services.AddHealthChecks().AddDbContextCheck<ZitbabaDbContext>("database");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Home/Error");

app.UseHttpsRedirection();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseStaticFiles();

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

await InitialiseDatabaseAsync(app);

app.Run();

static async Task InitialiseDatabaseAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();

    try
    {
        if (app.Environment.IsDevelopment())
        {
            // Convenience for local development only.
            await db.Database.MigrateAsync();
        }
        else if ((await db.Database.GetPendingMigrationsAsync()).Any())
        {
            // Never auto-migrate in production. Run the documented migration step first.
            logger.LogWarning("Pending migrations found. Skipping seed. Apply migrations, then restart.");
            return;
        }

        await DbSeeder.SeedAsync(app.Services);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database initialisation failed. The app will start, but /health will report Unhealthy.");
    }
}

// Needed so tests can reference the entry point if they ever use WebApplicationFactory.
public partial class Program;

