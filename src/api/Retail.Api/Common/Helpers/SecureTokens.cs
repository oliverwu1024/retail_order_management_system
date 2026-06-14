using System.Security.Cryptography;
using System.Text;

namespace Retail.Api.Common.Helpers;

/// <summary>
/// Small cryptographic helpers shared by the refresh-token and CSRF flows:
/// generating unguessable tokens, hashing them for at-rest storage, and
/// comparing secrets without leaking length/content via timing.
/// </summary>
/// <remarks>
/// Why a helper instead of inlining: these three operations are easy to get
/// subtly wrong (a non-CSPRNG, a non-constant-time compare). Putting them in one
/// reviewed place means every caller gets the safe version, and the unit tests
/// pin the behaviour once.
/// </remarks>
public static class SecureTokens
{
    /// <summary>
    /// Generates a URL-safe, cryptographically-random token. 32 bytes (256 bits)
    /// of entropy is well beyond brute-force reach and is the standard size for a
    /// session/refresh token.
    /// </summary>
    public static string NewToken(int numBytes = 32)
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(numBytes);
        return Base64UrlEncode(bytes);
    }

    /// <summary>
    /// SHA-256 hash of a token, as uppercase hex. We store this — never the raw
    /// token — so a database leak yields no usable credential. SHA-256 (not a
    /// slow password hash like PBKDF2) is correct here because the input is
    /// already high-entropy random; there is nothing to brute-force.
    /// </summary>
    public static string Sha256(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Constant-time string comparison. Used when comparing a presented secret
    /// against an expected one (CSRF token, hashes) so an attacker cannot infer
    /// how many leading characters matched from the response time.
    /// </summary>
    public static bool FixedTimeEquals(string a, string b)
    {
        byte[] ba = Encoding.UTF8.GetBytes(a);
        byte[] bb = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }

    // Base64url = base64 with the two URL-unsafe characters swapped and padding
    // stripped, so the token is safe in a cookie value without escaping.
    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
