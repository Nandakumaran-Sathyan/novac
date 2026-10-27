using SmartPOS.Application.Common;
using SmartPOS.Application.Sales;
using SmartPOS.Core.Enums;

namespace SmartPOS.UnitTests;

public class DiscountPolicyTests
{
    private static readonly PosOptions Options = new() { MaxCashierDiscountPercent = 10, MaxAdminDiscountPercent = 100 };

    [Fact]
    public void Cashier_within_limit_is_allowed() =>
        DiscountPolicy.EnsureAllowed(UserRole.Cashier, 10, Options);

    [Fact]
    public void Cashier_above_limit_is_forbidden() =>
        Assert.Throws<ForbiddenException>(() => DiscountPolicy.EnsureAllowed(UserRole.Cashier, 10.01m, Options));

    [Fact]
    public void Admin_uses_admin_limit()
    {
        DiscountPolicy.EnsureAllowed(UserRole.Admin, 50, Options);
        Assert.Equal(100, DiscountPolicy.MaxDiscountPercent(UserRole.Admin, Options));
        Assert.Equal(10, DiscountPolicy.MaxDiscountPercent(UserRole.Cashier, Options));
    }
}
