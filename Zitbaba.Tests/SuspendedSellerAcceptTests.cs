using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Zitbaba.Web.Data;
using Zitbaba.Web.Domain.Entities;
using Zitbaba.Web.Domain.Enums;
using Zitbaba.Web.Services;

namespace Zitbaba.Tests;

public class SuspendedSellerAcceptTests
{
    private static async Task<(Guid SellerUserId, Guid OrderId)> SeedAsync(
        IServiceScope scope, SellerApplicationStatus sellerStatus)
    {
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();
        var sellerUserId = Guid.NewGuid();
        var profile = new SellerProfile
        {
            UserId = sellerUserId,
            StoreName = "Store " + sellerUserId,
            ApplicationStatus = sellerStatus
        };
        db.SellerProfiles.Add(profile);

        var order = new Order
        {
            OrderNumber = "ZB-TEST-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            BuyerUserId = Guid.NewGuid(),
            SellerProfileId = profile.SellerProfileId,
            SubmissionKey = Guid.NewGuid(),
            Status = OrderStatus.Pending,
            TotalAmount = 10m
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (sellerUserId, order.OrderId);
    }

    [Fact]
    public async Task Suspended_seller_cannot_accept_a_new_order()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (sellerUserId, orderId) = await SeedAsync(scope, SellerApplicationStatus.Suspended);
        var transitions = scope.ServiceProvider.GetRequiredService<IOrderTransitionService>();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();

        var result = await transitions.SellerAcceptAsync(sellerUserId, orderId);

        Assert.Equal(TransitionOutcome.NotAllowed, result.Outcome);
        Assert.Equal(OrderStatus.Pending, (await db.Orders.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await db.OrderStatusHistory.ToListAsync());
    }

    [Fact]
    public async Task Approved_seller_can_accept_a_new_order()
    {
        using var host = new TestHost();
        using var scope = host.CreateScope();
        var (sellerUserId, orderId) = await SeedAsync(scope, SellerApplicationStatus.Approved);
        var transitions = scope.ServiceProvider.GetRequiredService<IOrderTransitionService>();
        var db = scope.ServiceProvider.GetRequiredService<ZitbabaDbContext>();

        var result = await transitions.SellerAcceptAsync(sellerUserId, orderId);

        Assert.Equal(TransitionOutcome.Ok, result.Outcome);
        Assert.Equal(OrderStatus.Accepted, (await db.Orders.AsNoTracking().SingleAsync()).Status);
    }
}
