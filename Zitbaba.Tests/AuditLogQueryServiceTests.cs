using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

public class AuditLogQueryServiceTests
{
    private static ZitbabaDbContext Db(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();

    private static AuditLogQueryService Service(IServiceScope scope) => new(Db(scope));

    private static ApplicationUser AddUser(ZitbabaDbContext db, string name, string tag)
    {
        var user = new ApplicationUser
        {
            UserName = tag + "@test.local",
            Email = tag + "@test.local",
            FullName = name,
            Phone = "097" + tag
        };
        db.Users.Add(user);
        return user;
    }

    private static AuditLog AddLog(
        ZitbabaDbContext db, string action, string entityType, DateTime when,
        Guid? actor = null, string? entityId = null, string? metadata = null)
    {
        var log = new AuditLog
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            ActorUserId = actor,
            Timestamp = when,
            SafeMetadata = metadata
        };
        db.AuditLogs.Add(log);
        return log;
    }

    [Fact]
    public async Task Newest_first_and_25_per_page_and_the_page_is_clamped()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 30; i++)
            AddLog(db, "ProductCreated", "Product", now.AddMinutes(-i), entityId: "P" + i);
        await db.SaveChangesAsync();
        var service = Service(scope);

        var page1 = await service.GetPageAsync(new AuditLogFilter(Page: 1));
        var page2 = await service.GetPageAsync(new AuditLogFilter(Page: 2));
        var tooFar = await service.GetPageAsync(new AuditLogFilter(Page: 99));
        var negative = await service.GetPageAsync(new AuditLogFilter(Page: -4));

        Assert.Equal(30, page1.Result.TotalCount);
        Assert.Equal(25, page1.Result.Items.Count);
        Assert.Equal("P0", page1.Result.Items[0].EntityId);
        Assert.Equal(5, page2.Result.Items.Count);
        Assert.Equal(2, tooFar.Result.Page);
        Assert.Equal(1, negative.Result.Page);
    }

    [Fact]
    public async Task Filters_by_action_and_by_entity_type_and_entity_id()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var now = DateTime.UtcNow;
        AddLog(db, "SellerApproved", "SellerProfile", now, entityId: "S1");
        AddLog(db, "SellerSuspended", "SellerProfile", now, entityId: "S1");
        AddLog(db, "ProductCreated", "Product", now, entityId: "P1");
        await db.SaveChangesAsync();
        var service = Service(scope);

        var byAction = await service.GetPageAsync(new AuditLogFilter(Action: "SellerSuspended"));
        var byType = await service.GetPageAsync(new AuditLogFilter(EntityType: "Product"));
        var byId = await service.GetPageAsync(new AuditLogFilter(EntityId: "S1"));
        var combined = await service.GetPageAsync(new AuditLogFilter(Action: "SellerApproved", EntityId: "S1"));

        Assert.Equal("SellerSuspended", Assert.Single(byAction.Result.Items).Action);
        Assert.Equal("ProductCreated", Assert.Single(byType.Result.Items).Action);
        Assert.Equal(2, byId.Result.TotalCount);
        Assert.Equal("SellerApproved", Assert.Single(combined.Result.Items).Action);
    }

    [Fact]
    public async Task Date_range_includes_the_whole_end_day_and_swaps_reversed_dates()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        AddLog(db, "Before", "X", new DateTime(2026, 10, 9, 23, 59, 0, DateTimeKind.Utc));
        AddLog(db, "StartDay", "X", new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc));
        AddLog(db, "EndDayLate", "X", new DateTime(2026, 10, 12, 23, 59, 59, DateTimeKind.Utc));
        AddLog(db, "After", "X", new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync();
        var service = Service(scope);
        var from = new DateTime(2026, 10, 10);
        var to = new DateTime(2026, 10, 12);

        var normal = await service.GetPageAsync(new AuditLogFilter(From: from, To: to));
        var reversed = await service.GetPageAsync(new AuditLogFilter(From: to, To: from));
        var onlyFrom = await service.GetPageAsync(new AuditLogFilter(From: to));
        var onlyTo = await service.GetPageAsync(new AuditLogFilter(To: from));

        var expected = new[] { "EndDayLate", "StartDay" };
        Assert.Equal(expected, normal.Result.Items.Select(i => i.Action).ToArray());
        Assert.Equal(expected, reversed.Result.Items.Select(i => i.Action).ToArray());
        Assert.Equal(new[] { "After", "EndDayLate" }, onlyFrom.Result.Items.Select(i => i.Action).ToArray());
        Assert.Equal(new[] { "StartDay", "Before" }, onlyTo.Result.Items.Select(i => i.Action).ToArray());
    }

    [Fact]
    public async Task Filters_by_actor_name_ignoring_case_and_excludes_rows_without_an_actor()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var alice = AddUser(db, "Alice Banda", "0001");
        var bob = AddUser(db, "Bob Phiri", "0002");
        await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        AddLog(db, "A1", "X", now, actor: alice.Id);
        AddLog(db, "B1", "X", now, actor: bob.Id);
        AddLog(db, "Sys", "X", now);
        await db.SaveChangesAsync();
        var service = Service(scope);

        var result = await service.GetPageAsync(new AuditLogFilter(Actor: "ALICE"));
        var none = await service.GetPageAsync(new AuditLogFilter(Actor: "nobody"));

        Assert.Equal("A1", Assert.Single(result.Result.Items).Action);
        Assert.Empty(none.Result.Items);
    }

    [Fact]
    public async Task Rows_show_the_actor_name_or_no_name_and_never_the_email_or_phone()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var alice = AddUser(db, "Alice Banda", "0003");
        await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        AddLog(db, "ByAlice", "X", now.AddMinutes(-2), actor: alice.Id);
        AddLog(db, "BySystem", "X", now.AddMinutes(-1));
        AddLog(db, "ByDeletedUser", "X", now, actor: Guid.NewGuid());
        await db.SaveChangesAsync();

        var rows = (await Service(scope).GetPageAsync(new AuditLogFilter())).Result.Items
            .ToDictionary(r => r.Action);

        Assert.Equal("Alice Banda", rows["ByAlice"].ActorName);
        Assert.Null(rows["BySystem"].ActorName);
        Assert.Null(rows["BySystem"].ActorUserId);
        Assert.Null(rows["ByDeletedUser"].ActorName);
        Assert.NotNull(rows["ByDeletedUser"].ActorUserId);
        var shape = typeof(AuditLogRow).GetProperties().Select(p => p.Name).ToArray();
        Assert.DoesNotContain("Email", shape);
        Assert.DoesNotContain("Phone", shape);
    }

    [Fact]
    public async Task Long_metadata_is_truncated_and_empty_metadata_is_null()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var now = DateTime.UtcNow;
        AddLog(db, "Long", "X", now, metadata: new string('a', 5000));
        AddLog(db, "Short", "X", now.AddMinutes(-1), metadata: "{\"id\":1}");
        AddLog(db, "Empty", "X", now.AddMinutes(-2), metadata: "   ");
        await db.SaveChangesAsync();

        var rows = (await Service(scope).GetPageAsync(new AuditLogFilter())).Result.Items
            .ToDictionary(r => r.Action);

        Assert.Equal(1003, rows["Long"].Metadata!.Length);
        Assert.EndsWith("...", rows["Long"].Metadata);
        Assert.Equal("{\"id\":1}", rows["Short"].Metadata);
        Assert.Null(rows["Empty"].Metadata);
    }

    [Fact]
    public async Task Action_and_entity_type_lists_are_distinct_and_sorted()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var db = Db(scope);
        var now = DateTime.UtcNow;
        AddLog(db, "Zeta", "Product", now);
        AddLog(db, "Alpha", "Order", now);
        AddLog(db, "Alpha", "Product", now);
        await db.SaveChangesAsync();

        var page = await Service(scope).GetPageAsync(new AuditLogFilter());

        Assert.Equal(new[] { "Alpha", "Zeta" }, page.Actions.ToArray());
        Assert.Equal(new[] { "Order", "Product" }, page.EntityTypes.ToArray());
    }
}
