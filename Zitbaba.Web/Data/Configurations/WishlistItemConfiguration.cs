using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zitbaba.Web.Domain.Entities;

namespace Zitbaba.Web.Data.Configurations;

public class WishlistItemConfiguration : IEntityTypeConfiguration<WishlistItem>
{
    public void Configure(EntityTypeBuilder<WishlistItem> b)
    {
        b.ToTable("WishlistItems");
        b.HasKey(w => w.WishlistItemId);

        // A user can save a product only once.
        b.HasIndex(w => new { w.UserId, w.ProductId }).IsUnique();
        b.HasIndex(w => w.ProductId);

        // Users are deactivated, never deleted. Restrict also avoids multiple cascade paths in SQL Server.
        b.HasOne(w => w.User)
            .WithMany()
            .HasForeignKey(w => w.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // A saved product goes away with its product, like images.
        b.HasOne(w => w.Product)
            .WithMany()
            .HasForeignKey(w => w.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
