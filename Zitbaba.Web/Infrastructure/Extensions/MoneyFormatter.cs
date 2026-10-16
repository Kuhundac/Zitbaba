using System.Globalization;

namespace Zitbaba.Web.Infrastructure.Extensions;

/// <summary>The one place money is formatted for display: "ZMW 120.00".</summary>
public static class MoneyFormatter
{
    public static string Zmw(decimal amount) =>
        "ZMW " + amount.ToString("N2", CultureInfo.InvariantCulture);
}
