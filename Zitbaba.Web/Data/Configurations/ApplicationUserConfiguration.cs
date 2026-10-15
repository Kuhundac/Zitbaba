using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zitbaba.Web.Domain.Entities;

namespace Zitbaba.Web.Data.Configurations;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> b)
    {
        b.Property(u => u.FullName).IsRequired().HasMaxLength(120);
        b.Property(u => u.Phone).IsRequired().HasMaxLength(20);
        b.Property(u => u.ProfileImageUrl).HasMaxLength(500);
        b.Property(u => u.Programme).HasMaxLength(120);
        b.Property(u => u.ResidenceOrRoom).HasMaxLength(120);

        b.Property(u => u.PhoneVisibility).HasConversion<string>().HasMaxLength(30);
        b.Property(u => u.ResidenceVisibility).HasConversion<string>().HasMaxLength(30);
        b.Property(u => u.AccountStatus).HasConversion<string>().HasMaxLength(30);

        // Unique phone number.
        b.HasIndex(u => u.Phone).IsUnique().HasDatabaseName("UX_AspNetUsers_Phone");

        // Identity already creates "EmailIndex" on NormalizedEmail (non-unique). Make it unique.
        b.HasIndex(u => u.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();

        b.HasOne(u => u.SellerProfile)
            .WithOne(s => s.User)
            .HasForeignKey<SellerProfile>(s => s.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
