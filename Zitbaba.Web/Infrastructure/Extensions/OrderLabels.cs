using System.Globalization;
using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Infrastructure.Extensions;

/// <summary>Plain-language text for order states and times (Zambia time).</summary>
public static class OrderLabels
{
    private static readonly TimeZoneInfo Zambia = ResolveZone();

    private static TimeZoneInfo ResolveZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Lusaka"); }
        catch (Exception) { return TimeZoneInfo.CreateCustomTimeZone("CAT", TimeSpan.FromHours(2), "CAT", "CAT"); }
    }

    public static string Status(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "Waiting for the seller",
        OrderStatus.Accepted => "Accepted",
        OrderStatus.Rejected => "Rejected",
        OrderStatus.ReadyForPickup => "Ready for pickup",
        OrderStatus.Completed => "Completed",
        OrderStatus.CancelledByBuyer => "Cancelled",
        _ => status.ToString()
    };

    public static string Payment(PaymentStatus status) =>
        status == PaymentStatus.Paid ? "Paid" : "Cash on pickup (not paid yet)";

    public static string When(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zambia)
            .ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
}
