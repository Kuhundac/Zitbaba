using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zitbaba.Web.Domain.Entities;

namespace Zitbaba.Web.Data.Configurations;

public class SellerProfileConfiguration : IEntityTypeConfiguration<SellerProfile>
{
    public void Configure(EntityTypeBuilder<SellerProfile> b)
    {
        b.ToTable("SellerProfiles");
        b.HasKey(s => s.SellerProfileId);

        b.Property(s => s.StoreName).IsRequired().HasMaxLength(120);
        b.Property(s => s.Description).HasMaxLength(1000);
        b.Property(s => s.StoreImageUrl).HasMaxLength(500);
        b.Property(s => s.ApplicationNotes).HasMaxLength(1000);
        b.Property(s => s.ReviewReason).HasMaxLength(500);
        b.Property(s => s.ApplicationStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(s => s.RatingAverage).HasPrecision(3, 2);

        b.HasIndex(s => s.UserId).IsUnique();
        b.HasIndex(s => s.StoreName).IsUnique();
        b.HasIndex(s => s.ApplicationStatus);

        b.HasOne(s => s.ReviewedByUser)
            .WithMany()
            .HasForeignKey(s => s.ReviewedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
