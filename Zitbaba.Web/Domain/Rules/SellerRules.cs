using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Domain.Rules;

public static class SellerRules
{
    /// <summary>Only an Approved seller profile may publish products.</summary>
    public static bool CanSell(SellerApplicationStatus status) =>
        status == SellerApplicationStatus.Approved;
}
