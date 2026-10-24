using Zitbaba.Web.Services;

namespace Zitbaba.Web.Models;

public record CheckoutPageModel(CartView Cart, Guid SubmissionKey);
