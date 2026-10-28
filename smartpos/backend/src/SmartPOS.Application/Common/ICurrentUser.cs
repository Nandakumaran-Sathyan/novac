namespace SmartPOS.Application.Common;

/// <summary>The authenticated caller, resolved from the JWT by the API layer.</summary>
public interface ICurrentUser
{
    int UserId { get; }
    /// <summary>The shop the caller belongs to. All data access is scoped to it.</summary>
    int ShopId { get; }
}

/// <summary>
/// The shop of the current request, or null when there is none (startup seeding, token validation,
/// login). The DbContext uses it for its tenant query filters; with no shop those filters match
/// nothing, so a missing shop can never expose another shop's rows.
/// </summary>
public interface ICurrentShop
{
    int? ShopId { get; }
}
