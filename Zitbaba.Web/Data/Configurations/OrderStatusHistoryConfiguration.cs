using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zitbaba.Web.Domain.Entities;

namespace Zitbaba.Web.Data.Configurations;

public class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> b)
    {
        b.ToTable("OrderStatusHistory");
        b.HasKey(h => h.OrderStatusHistoryId);

        b.Property(h => h.FromStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(h => h.ToStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(h => h.Reason).HasMaxLength(500);

        b.HasIndex(h => new { h.OrderId, h.CreatedAt });

        b.HasOne(h => h.Order)
            .WithMany(o => o.History)
            .HasForeignKey(h => h.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
