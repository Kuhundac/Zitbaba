using Zitbaba.Web.Services;

namespace Zitbaba.Web.Models;

public record HomeIndexModel(
    IReadOnlyList<CatalogueItem> Recent,
    IReadOnlyList<CatalogueCategory> Categories);

public record SearchPageModel(
    CatalogueQuery Query,
    PagedResult<CatalogueItem> Result,
    IReadOnlyList<CatalogueCategory> Categories);
