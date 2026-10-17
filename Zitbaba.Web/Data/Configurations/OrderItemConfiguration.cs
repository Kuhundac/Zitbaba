using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zitbaba.Web.Domain.Entities;

namespace Zitbaba.Web.Data.Configurations;

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.ToTable("OrderItems", t =>
        {
            t.HasCheckConstraint("CK_OrderItems_Quantity_Positive", "[Quantity] > 0");
            t.HasCheckConstraint("CK_OrderItems_UnitPrice_NonNegative", "[UnitPrice] >= 0");
        });
        b.HasKey(i => i.OrderItemId);

        b.Property(i => i.ProductName).IsRequired().HasMaxLength(160);
        b.Property(i => i.UnitPrice).HasPrecision(18, 2);
        b.Property(i => i.LineTotal).HasPrecision(18, 2);

        b.HasIndex(i => i.OrderId);
        b.HasIndex(i => i.ProductId);

        b.HasOne(i => i.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
