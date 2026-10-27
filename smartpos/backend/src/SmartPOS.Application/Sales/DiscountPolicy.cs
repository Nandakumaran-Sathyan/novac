using SmartPOS.Application.Common;
using SmartPOS.Core.Enums;

namespace SmartPOS.Application.Sales;

public static class DiscountPolicy
{
    public static decimal MaxDiscountPercent(UserRole role, PosOptions options) =>
        role == UserRole.Admin ? options.MaxAdminDiscountPercent : options.MaxCashierDiscountPercent;

    /// <summary>Throws <see cref="ForbiddenException"/> if the role may not give this discount.</summary>
    public static void EnsureAllowed(UserRole role, decimal discountPercent, PosOptions options)
    {
        var max = MaxDiscountPercent(role, options);
        if (discountPercent > max)
            throw new ForbiddenException($"A {role} can apply a discount of at most {max:0.##}%.");
    }
}
