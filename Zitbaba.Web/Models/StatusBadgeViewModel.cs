namespace Zitbaba.Web.Models;

public enum BadgeKind
{
    Info,
    Success,
    Warning,
    Error
}

public record StatusBadgeViewModel(string Label, BadgeKind Kind = BadgeKind.Info);
