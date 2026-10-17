using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zitbaba.Web.Domain.Entities;

namespace Zitbaba.Web.Data.Configurations;

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> b)
    {
        b.ToTable("Carts");
        b.HasKey(c => c.CartId);

        // One cart per user.
        b.HasIndex(c => c.UserId).IsUnique();

        b.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(c => c.SellerProfile)
            .WithMany()
            .HasForeignKey(c => c.SellerProfileId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
