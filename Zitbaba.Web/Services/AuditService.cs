using System.Diagnostics;
using System.Text.Json;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;

namespace Zitbaba.Web.Services;

public interface IAuditService
{
    /// <summary>Adds an audit row to the current DbContext WITHOUT saving, so the caller's
    /// SaveChanges and transaction commit it together with the change being audited.</summary>
    void Stage(Guid? actorUserId, string action, string entityType, string? entityId, object? metadata = null);

    /// <summary>Adds an audit row and saves immediately (for events with no other change, e.g. AccessDenied).</summary>
    Task WriteAsync(Guid? actorUserId, string action, string entityType, string? entityId, object? metadata = null);
}

public class AuditService : IAuditService
{
    private readonly ZitbabaDbContext _db;

    public AuditService(ZitbabaDbContext db) => _db = db;

    public void Stage(Guid? actorUserId, string action, string entityType, string? entityId, object? metadata = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Timestamp = DateTime.UtcNow,
            CorrelationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N"),
            // Callers pass ids, statuses and admin-written reasons only: never passwords, tokens or contact details.
            SafeMetadata = metadata is null ? null : JsonSerializer.Serialize(metadata)
        });
    }

    public async Task WriteAsync(Guid? actorUserId, string action, string entityType, string? entityId, object? metadata = null)
    {
        Stage(actorUserId, action, entityType, entityId, metadata);
        await _db.SaveChangesAsync();
    }
}
