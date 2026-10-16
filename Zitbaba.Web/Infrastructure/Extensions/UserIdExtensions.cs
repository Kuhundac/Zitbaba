using System.Security.Claims;

namespace Zitbaba.Web.Infrastructure.Extensions;

public static class UserIdExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
