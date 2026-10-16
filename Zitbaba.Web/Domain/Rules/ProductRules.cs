using Zitbaba.Web.Domain.Enums;

namespace Zitbaba.Web.Domain.Rules;

public static class ProductRules
{
    public static ProductStatus InitialStatus(bool requireModeration) =>
        requireModeration ? ProductStatus.PendingModeration : ProductStatus.Active;

    public static bool CanEdit(ProductStatus s) =>
        s is ProductStatus.Draft or ProductStatus.Active or ProductStatus.Inactive or ProductStatus.PendingModeration;

    public static bool CanDeactivate(ProductStatus s) =>
        s is ProductStatus.Active or ProductStatus.PendingModeration;

    public static bool CanReactivate(ProductStatus s) => s is ProductStatus.Inactive;

    public static bool CanRemove(ProductStatus s) =>
        s is ProductStatus.Draft or ProductStatus.Active or ProductStatus.Inactive or ProductStatus.PendingModeration;
}
