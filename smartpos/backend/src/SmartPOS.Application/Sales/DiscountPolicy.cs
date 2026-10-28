using SmartPOS.Application.Common;

namespace SmartPOS.Application.Sales;

public static class DiscountPolicy
{
    public static decimal MaxDiscountPercent(PosOptions options) => options.MaxDiscountPercent;

    /// <summary>Throws <see cref="ForbiddenException"/> if the discount is above the configured limit.</summary>
    public static void EnsureAllowed(decimal discountPercent, PosOptions options)
    {
        var max = MaxDiscountPercent(options);
        if (discountPercent > max)
            throw new ForbiddenException($"A discount can be at most {max:0.##}%.");
    }
}
