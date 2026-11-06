using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SmartPOS.Application.Common;
using SmartPOS.Core.Entities;

namespace SmartPOS.Application.Customers;

public sealed record CustomerDto(int Id, string Name, string Phone, int LoyaltyPoints, DateTime CreatedAt);

/// <summary>
/// Indian mobile numbers: 10 digits starting with 6, 7, 8 or 9, optionally written with +91, 91 or 0 in front
/// and with spaces or hyphens. Stored as the bare 10 digits, so every way of writing a number is the same customer.
/// [0-9] rather than \d on purpose: \d also matches other scripts' digits (e.g. Devanagari).
/// </summary>
public static partial class PhoneRules
{
    public const string Pattern = @"^(?:\+91|91|0)?[ -]?[6-9](?:[ -]?[0-9]){9}$";
    public const string Message = "Enter a 10-digit Indian mobile number starting with 6, 7, 8 or 9 (+91 or 0 in front is fine).";

    /// <summary>"+91 98765-43210", "919876543210" and "09876543210" all become "9876543210". Other input keeps just its digits.</summary>
    public static string Normalize(string phone)
    {
        var trimmed = phone.Trim();
        if (trimmed.StartsWith("+91", StringComparison.Ordinal))
            trimmed = trimmed[3..];
        var digits = new string(trimmed.Where(char.IsAsciiDigit).ToArray());
        return digits.Length switch
        {
            12 when digits.StartsWith("91", StringComparison.Ordinal) => digits[2..],
            11 when digits[0] == '0' => digits[1..],
            _ => digits
        };
    }

    /// <summary>A search term made only of phone characters ("+91 98765", "98765-4") is searched by its digits.</summary>
    public static bool LooksLikePhone(string term) => PhoneCharacters().IsMatch(term);

    [GeneratedRegex(@"^\+?[0-9][0-9 -]*$")]
    private static partial Regex PhoneCharacters();
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
    private static CustomerDto ToDto(Customer c) => new(c.Id, c.Name, c.Phone, c.LoyaltyPoints, c.CreatedAt);

    public async Task<PagedResult<CustomerDto>> ListAsync(CustomerQuery query, CancellationToken cancellationToken)
    {
        var customers = db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var pattern = SearchPattern.Contains(PhoneRules.LooksLikePhone(term) ? PhoneRules.Normalize(term) : term);
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
        var phone = PhoneRules.Normalize(request.Phone);
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

        var phone = PhoneRules.Normalize(request.Phone);
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
