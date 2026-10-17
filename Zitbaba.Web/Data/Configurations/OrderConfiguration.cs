using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zitbaba.Web.Domain.Entities;

namespace Zitbaba.Web.Data.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("Orders", t =>
        {
            t.HasCheckConstraint("CK_Orders_Total_NonNegative", "[TotalAmount] >= 0");
        });
        b.HasKey(o => o.OrderId);

        b.Property(o => o.OrderNumber).IsRequired().HasMaxLength(30);
        b.Property(o => o.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(o => o.PaymentStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(o => o.PaymentMethod).HasConversion<string>().HasMaxLength(30);
        b.Property(o => o.FulfilmentMethod).HasConversion<string>().HasMaxLength(30);
        b.Property(o => o.TotalAmount).HasPrecision(18, 2);

        b.HasIndex(o => o.OrderNumber).IsUnique();

        // Duplicate-submit protection: the same buyer cannot reuse a checkout key.
        b.HasIndex(o => new { o.BuyerUserId, o.SubmissionKey }).IsUnique();

        b.HasIndex(o => new { o.BuyerUserId, o.CreatedAt }).IsDescending(false, true);
        b.HasIndex(o => new { o.SellerProfileId, o.Status, o.CreatedAt });

        // Financial data: never cascade.
        b.HasOne(o => o.Buyer)
            .WithMany()
            .HasForeignKey(o => o.BuyerUserId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(o => o.SellerProfile)
            .WithMany()
            .HasForeignKey(o => o.SellerProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
