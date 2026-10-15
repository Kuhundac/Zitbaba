using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zitbaba.Web.Domain.Entities;

namespace Zitbaba.Web.Data.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("Products", t =>
        {
            t.HasCheckConstraint("CK_Products_Price_NonNegative", "[Price] >= 0");
            t.HasCheckConstraint("CK_Products_Quantity_NonNegative", "[QuantityAvailable] >= 0");
        });
        b.HasKey(p => p.ProductId);

        b.Property(p => p.Name).IsRequired().HasMaxLength(160);
        b.Property(p => p.Description).HasMaxLength(2000);
        b.Property(p => p.Price).HasPrecision(18, 2);
        b.Property(p => p.Condition).HasConversion<string>().HasMaxLength(30);
        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(p => p.RowVersion).IsRowVersion();

        b.HasOne(p => p.SellerProfile)
            .WithMany(s => s.Products)
            .HasForeignKey(p => p.SellerProfileId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(p => new { p.CategoryId, p.Status });
        b.HasIndex(p => new { p.SellerProfileId, p.Status });
        b.HasIndex(p => new { p.Status, p.CreatedAt }).IsDescending(false, true);
    }
}
