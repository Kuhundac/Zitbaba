using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Models;

public static class AccountStatusBadges
{
    public static StatusBadgeViewModel For(AccountStatus status) => status switch
    {
        AccountStatus.Active => new StatusBadgeViewModel("Active", BadgeKind.Success),
        AccountStatus.Suspended => new StatusBadgeViewModel("Suspended", BadgeKind.Error),
        _ => new StatusBadgeViewModel("Deactivated", BadgeKind.Warning)
    };
}
