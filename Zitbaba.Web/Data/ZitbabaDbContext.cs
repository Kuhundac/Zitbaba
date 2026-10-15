using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Domain.Entities;

namespace Zitbaba.Web.Data;

public class ZitbabaDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public ZitbabaDbContext(DbContextOptions<ZitbabaDbContext> options) : base(options)
    {
    }

    public DbSet<SellerProfile> SellerProfiles => Set<SellerProfile>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity mappings first, so our configurations can adjust them (e.g. unique email index).
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ZitbabaDbContext).Assembly);
    }
}
