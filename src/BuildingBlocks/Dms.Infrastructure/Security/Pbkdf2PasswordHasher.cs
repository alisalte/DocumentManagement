using System.Security.Cryptography;
using System.Text;
using Dms.Application;

namespace Dms.Infrastructure.Security;

/// <summary>
/// PBKDF2-HMAC-SHA512, written out rather than taking a dependency on ASP.NET Core Identity for one
/// method. Format: <c>v1.iterations.salt.hash</c> (base64), so the iteration count can be raised
/// later and old hashes still verify.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int DefaultIterations = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            DefaultIterations,
            HashAlgorithmName.SHA512,
            KeySize);

        return $"v1.{DefaultIterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string password, string hash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hash))
        {
            return false;
        }

        var parts = hash.Split('.');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            iterations,
            HashAlgorithmName.SHA512,
            expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

/// <summary>
/// Refresh tokens and share-link tokens. 256 bits of entropy from a CSPRNG, stored as a
/// SHA-256 digest: with that much entropy a password-style KDF would only slow lookups down.
/// </summary>
public sealed class SecureTokenGenerator : ISecureTokenGenerator
{
    private const int TokenBytes = 32;

    public GeneratedToken Create()
    {
        var raw = RandomNumberGenerator.GetBytes(TokenBytes);
        var value = Base64UrlEncode(raw);
        return new GeneratedToken(value, ComputeHash(value));
    }

    public byte[] ComputeHash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
