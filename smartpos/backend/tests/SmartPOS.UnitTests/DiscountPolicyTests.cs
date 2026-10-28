using SmartPOS.Application.Common;
using SmartPOS.Application.Sales;

namespace SmartPOS.UnitTests;

public class DiscountPolicyTests
{
    private static readonly PosOptions Options = new() { MaxDiscountPercent = 25 };

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(25)]
    public void Discount_within_limit_is_allowed(decimal percent) =>
        DiscountPolicy.EnsureAllowed(percent, Options);

    [Fact]
    public void Discount_above_limit_is_forbidden() =>
        Assert.Throws<ForbiddenException>(() => DiscountPolicy.EnsureAllowed(25.01m, Options));

    [Fact]
    public void Limit_comes_from_configuration() =>
        Assert.Equal(25, DiscountPolicy.MaxDiscountPercent(Options));
}
