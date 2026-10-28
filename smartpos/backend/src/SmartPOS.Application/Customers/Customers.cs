using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Application.Common;
using SmartPOS.Core.Entities;

namespace SmartPOS.Application.Customers;

public sealed record CustomerDto(int Id, string Name, string Phone, int LoyaltyPoints, DateTime CreatedAt);

public static class PhoneRules
{
    /// <summary>7–20 characters: digits, spaces, '+', '-', '(' and ')'.</summary>
    public const string Pattern = @"^\+?[0-9][0-9 ()-]{5,18}[0-9]$";
    public const string Message = "Phone must be 7-20 characters: digits, spaces, '+', '-', '(' and ')'.";
}

/// <summary>Fields shared by create and update. Loyalty points are not editable here.</summary>
public abstract class CustomerRequestBase
{
    [Required, StringLength(100, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;

    [Required, RegularExpression(PhoneRules.Pattern, ErrorMessage = PhoneRules.Message)]
    public string Phone { get; init; } = string.Empty;
}

public sealed class CreateCustomerRequest : CustomerRequestBase;

public sealed class UpdateCustomerRequest : CustomerRequestBase;

public sealed class CustomerQuery : PagedQuery
{
    /// <summary>Matches name or phone.</summary>
    [StringLength(100)]
    public string? Search { get; init; }
}

public interface ICustomerService
{
    Task<PagedResult<CustomerDto>> ListAsync(CustomerQuery query, CancellationToken cancellationToken);
    Task<CustomerDto> GetAsync(int id, CancellationToken cancellationToken);
    Task<CustomerDto> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken);
    Task<CustomerDto> UpdateAsync(int id, UpdateCustomerRequest request, CancellationToken cancellationToken);
}

public sealed class CustomerService(IApplicationDbContext db, ICurrentUser currentUser, StoreCalendar calendar) : ICustomerService
{
    /// <summary>Keeps the digits and a leading '+', so "98765 43210" and "9876543210" are the same number.</summary>
    public static string NormalizePhone(string phone)
    {
        var trimmed = phone.Trim();
        var digits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());
        return trimmed.StartsWith('+') ? "+" + digits : digits;
    }

    private static CustomerDto ToDto(Customer c) => new(c.Id, c.Name, c.Phone, c.LoyaltyPoints, c.CreatedAt);

    public async Task<PagedResult<CustomerDto>> ListAsync(CustomerQuery query, CancellationToken cancellationToken)
    {
        var customers = db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = SearchPattern.Contains(query.Search);
            customers = customers.Where(c =>
                EF.Functions.Like(c.Name.ToLower(), pattern, SearchPattern.EscapeCharacter) ||
                EF.Functions.Like(c.Phone.ToLower(), pattern, SearchPattern.EscapeCharacter));
        }

        return await customers
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => new CustomerDto(c.Id, c.Name, c.Phone, c.LoyaltyPoints, c.CreatedAt))
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<CustomerDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw NotFoundException.For("Customer", id);
        return ToDto(customer);
    }

    public async Task<CustomerDto> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var phone = NormalizePhone(request.Phone);
        await EnsurePhoneIsFree(phone, excludeCustomerId: null, cancellationToken);

        var customer = new Customer
        {
            ShopId = currentUser.ShopId,
            Name = request.Name.Trim(),
            Phone = phone,
            CreatedAt = calendar.UtcNow
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(customer);
    }

    public async Task<CustomerDto> UpdateAsync(int id, UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
                       ?? throw NotFoundException.For("Customer", id);

        var phone = NormalizePhone(request.Phone);
        if (phone != customer.Phone)
            await EnsurePhoneIsFree(phone, customer.Id, cancellationToken);

        customer.Name = request.Name.Trim();
        customer.Phone = phone;
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(customer);
    }

    private async Task EnsurePhoneIsFree(string phone, int? excludeCustomerId, CancellationToken cancellationToken)
    {
        if (await db.Customers.AnyAsync(c => c.Phone == phone && c.Id != excludeCustomerId, cancellationToken))
            throw new ConflictException($"A customer with phone '{phone}' already exists.");
    }
}
