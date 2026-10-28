namespace SmartPOS.Core.Enums;

public enum SaleStatus
{
    /// <summary>Reserved for bills that are started but not paid. The checkout flow does not create these yet.</summary>
    Pending = 1,
    /// <summary>Paid, with stock deducted.</summary>
    Completed = 2,
    /// <summary>Voided. Reports ignore cancelled sales. There is no cancel endpoint yet.</summary>
    Cancelled = 3
}
