using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SmartPOS.Application.Auth;
using SmartPOS.Core.Entities;

namespace SmartPOS.API.Auth;

public static class SmartPosClaims
{
    public const string UserId = JwtRegisteredClaimNames.Sub;
    public const string Name = JwtRegisteredClaimNames.Name;
    public const string Email = JwtRegisteredClaimNames.Email;
    public const string ShopId = "shop";
    /// <summary>Added on each request from the database (never put in the token), so verifying takes effect at once.</summary>
    public const string EmailVerified = "email_verified";
}

public static class SmartPosPolicies
{
    /// <summary>Signed in, email verified or not: only the endpoints an unverified user needs to finish verifying.</summary>
    public const string SignedIn = "signed-in";
}

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    private readonly JsonWebTokenHandler _handler = new();

    public static SymmetricSecurityKey CreateKey(string signingKey) => new(Encoding.UTF8.GetBytes(signingKey));

    public (string Token, DateTime ExpiresAt) CreateToken(User user)
    {
        var jwt = options.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(jwt.ExpiryMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            Claims = new Dictionary<string, object>
            {
                [SmartPosClaims.UserId] = user.Id.ToString(),
                [SmartPosClaims.Name] = user.FullName,
                [SmartPosClaims.Email] = user.Email,
                [SmartPosClaims.ShopId] = user.ShopId.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N")
            },
            SigningCredentials = new SigningCredentials(CreateKey(jwt.SigningKey), SecurityAlgorithms.HmacSha256)
        };
        return (_handler.CreateToken(descriptor), expires);
    }
}
