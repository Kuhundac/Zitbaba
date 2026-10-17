using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zitbaba.Web.Domain.Entities;

namespace Zitbaba.Web.Data.Configurations;

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> b)
    {
        b.ToTable("CartItems", t =>
        {
            t.HasCheckConstraint("CK_CartItems_Quantity_Positive", "[Quantity] > 0");
        });
        b.HasKey(i => i.CartItemId);

        // A product appears once per cart; changing quantity updates the same row.
        b.HasIndex(i => new { i.CartId, i.ProductId }).IsUnique();

        b.HasOne(i => i.Cart)
            .WithMany(c => c.Items)
            .HasForeignKey(i => i.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        // Products are never hard-deleted. Restrict also avoids multiple cascade paths.
        b.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
