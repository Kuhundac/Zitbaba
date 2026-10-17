namespace Zitbaba.Web.Domain.Enums;

/// <summary>An order is never treated as paid just because it was created.</summary>
public enum PaymentStatus
{
    Pending = 0,
    Paid = 1
}
