namespace Zitbaba.Web.Domain.Entities;

public class AuditLog
{
    public long AuditLogId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? CorrelationId { get; set; }

    /// <summary>JSON string. Never store passwords, tokens or other secrets here.</summary>
    public string? SafeMetadata { get; set; }
}
