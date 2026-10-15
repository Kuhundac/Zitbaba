using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zitbaba.Web.Domain.Entities;

namespace Zitbaba.Web.Data.Configurations;

public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> b)
    {
        b.ToTable("ProductImages");
        b.HasKey(i => i.ImageId);
        b.Property(i => i.ImageUrl).IsRequired().HasMaxLength(500);
        b.Property(i => i.ThumbnailUrl).HasMaxLength(500);

        // Images are not financial/auditable data, so they go away with their product.
        b.HasOne(i => i.Product)
            .WithMany(p => p.Images)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(i => new { i.ProductId, i.SortOrder });
    }
}
