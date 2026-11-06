using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SmartPOS.Application.Auth;

namespace SmartPOS.API.Auth;

/// <summary>
/// HMAC-SHA256 of "userId:code". The key is derived from the JWT signing key with HKDF and a purpose label, so
/// no new secret has to be configured and the two uses never share a key.
/// </summary>
public sealed class VerificationCodeHasher(IOptions<JwtOptions> options) : IVerificationCodeHasher
{
    private readonly byte[] _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(options.Value.SigningKey),
        32, info: Encoding.UTF8.GetBytes("smartpos:email-verification-code"));

    public string Hash(int userId, string code) =>
        Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{userId}:{code}")));
}
