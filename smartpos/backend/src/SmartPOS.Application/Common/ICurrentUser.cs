using SmartPOS.Core.Enums;

namespace SmartPOS.Application.Common;

/// <summary>The authenticated caller, resolved from the JWT by the API layer.</summary>
public interface ICurrentUser
{
    int UserId { get; }
    UserRole Role { get; }
    bool IsAdmin => Role == UserRole.Admin;
}
