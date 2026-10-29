using Microsoft.EntityFrameworkCore;
using Zitbaba.Web.Data;

namespace Zitbaba.Web.Services;

public record AuditLogFilter(
    string? Action = null,
    string? EntityType = null,
    string? EntityId = null,
    string? Actor = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1);

// Actor is shown by full name only. No email or phone number.
public record AuditLogRow(
    long AuditLogId,
    DateTime Timestamp,
    string Action,
    string EntityType,
    string? EntityId,
    Guid? ActorUserId,
    string? ActorName,
    string? Metadata);

public record AuditLogPageModel(
    AuditLogFilter Filter,
    PagedResult<AuditLogRow> Result,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> EntityTypes);

public interface IAuditLogQueryService
{
    Task<AuditLogPageModel> GetPageAsync(AuditLogFilter filter);
}

public class AuditLogQueryService : IAuditLogQueryService
{
    public const int PageSize = 25;
    private const int MaxMetadataLength = 1000;
    private const int MaxTextLength = 100;

    private readonly ZitbabaDbContext _db;

    public AuditLogQueryService(ZitbabaDbContext db) => _db = db;

    public async Task<AuditLogPageModel> GetPageAsync(AuditLogFilter filter)
    {
        var query =
            from log in _db.AuditLogs.AsNoTracking()
            join user in _db.Users.AsNoTracking() on log.ActorUserId equals (Guid?)user.Id into actors
            from actor in actors.DefaultIfEmpty()
            select new { Log = log, ActorName = actor != null ? actor.FullName : null };

        var action = Clean(filter.Action);
        if (action is not null) query = query.Where(x => x.Log.Action == action);

        var entityType = Clean(filter.EntityType);
        if (entityType is not null) query = query.Where(x => x.Log.EntityType == entityType);

        var entityId = Clean(filter.EntityId);
        if (entityId is not null) query = query.Where(x => x.Log.EntityId == entityId);

        var actorTerm = Clean(filter.Actor)?.ToLowerInvariant();
        if (actorTerm is not null)
            query = query.Where(x => x.ActorName != null && x.ActorName.ToLower().Contains(actorTerm));

        var from = filter.From;
        var to = filter.To;
        if (from is DateTime a && to is DateTime b && a > b) (from, to) = (b, a);
        if (from is DateTime start)
        {
            var startDay = start.Date;
            query = query.Where(x => x.Log.Timestamp >= startDay);
        }
        if (to is DateTime end)
        {
            var endExclusive = end.Date.AddDays(1);
            query = query.Where(x => x.Log.Timestamp < endExclusive);
        }

        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        var page = Math.Clamp(filter.Page, 1, pages);

        var rows = await query
            .OrderByDescending(x => x.Log.Timestamp).ThenByDescending(x => x.Log.AuditLogId)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(x => new
            {
                x.Log.AuditLogId,
                x.Log.Timestamp,
                x.Log.Action,
                x.Log.EntityType,
                x.Log.EntityId,
                x.Log.ActorUserId,
                x.ActorName,
                x.Log.SafeMetadata
            })
            .ToListAsync();

        var items = rows.Select(r => new AuditLogRow(
            r.AuditLogId, r.Timestamp, r.Action, r.EntityType, r.EntityId, r.ActorUserId, r.ActorName,
            Truncate(r.SafeMetadata))).ToList();

        var actions = await _db.AuditLogs.AsNoTracking()
            .Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();
        var entityTypes = await _db.AuditLogs.AsNoTracking()
            .Select(a => a.EntityType).Distinct().OrderBy(a => a).ToListAsync();

        return new AuditLogPageModel(
            filter with { Page = page },
            new PagedResult<AuditLogRow>(items, page, PageSize, total),
            actions, entityTypes);
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        return text.Length > MaxTextLength ? text[..MaxTextLength] : text;
    }

    private static string? Truncate(string? metadata)
    {
        if (string.IsNullOrWhiteSpace(metadata)) return null;
        return metadata.Length > MaxMetadataLength ? metadata[..MaxMetadataLength] + "..." : metadata;
    }
}
