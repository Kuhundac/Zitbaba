namespace Zitbaba.Web.Infrastructure.Security;

public static class RoleNames
{
    public const string Buyer = "Buyer";
    public const string Seller = "Seller";
    public const string Admin = "Admin";
    public const string Support = "Support";
    public const string DeliveryAgent = "DeliveryAgent";

    public static readonly string[] All = { Buyer, Seller, Admin, Support, DeliveryAgent };

    /// <summary>Roles that can never be granted through self-service registration.</summary>
    public static readonly string[] NeverSelfAssigned = { Admin, DeliveryAgent, Support, Seller };
}
