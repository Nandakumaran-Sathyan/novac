namespace SmartPOS.Application.Auth;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    /// <summary>Creates a shop and its owner, and signs them in.</summary>
    Task<LoginResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<UserDto> GetCurrentAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Adds the sample catalogue to a newly created shop when sample data is switched on
/// (Database:SeedSampleData, Development only). Does nothing otherwise.
/// </summary>
public interface ISampleDataSeeder
{
    Task SeedAsync(int shopId, CancellationToken cancellationToken);
}
