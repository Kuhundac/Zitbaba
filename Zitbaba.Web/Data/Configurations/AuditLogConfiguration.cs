using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Zitbaba.Web.Domain.Entities;

namespace Zitbaba.Web.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.HasKey(a => a.AuditLogId);
        b.Property(a => a.Action).IsRequired().HasMaxLength(100);
        b.Property(a => a.EntityType).IsRequired().HasMaxLength(100);
        b.Property(a => a.EntityId).HasMaxLength(64);
        b.Property(a => a.CorrelationId).HasMaxLength(64);
        // No FK on ActorUserId on purpose: the audit trail must survive any user changes.

        b.HasIndex(a => a.Timestamp);
        b.HasIndex(a => new { a.EntityType, a.EntityId });
    }
}
