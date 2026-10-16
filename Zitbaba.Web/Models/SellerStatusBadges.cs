using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Models;

public static class SellerStatusBadges
{
    public static StatusBadgeViewModel For(SellerApplicationStatus status) => status switch
    {
        SellerApplicationStatus.Pending => new StatusBadgeViewModel("Pending", BadgeKind.Warning),
        SellerApplicationStatus.Approved => new StatusBadgeViewModel("Approved", BadgeKind.Success),
        SellerApplicationStatus.Rejected => new StatusBadgeViewModel("Rejected", BadgeKind.Error),
        SellerApplicationStatus.Suspended => new StatusBadgeViewModel("Suspended", BadgeKind.Error),
        _ => new StatusBadgeViewModel(status.ToString())
    };
}
