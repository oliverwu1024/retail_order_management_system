using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Retail.Api.Common.Helpers;

namespace Retail.Api.Identity;

/// <summary>
/// Signed double-submit CSRF tokens. The token is <c>{random}.{signature}</c>
/// where <c>signature = base64url(HMACSHA256(serverKey, random))</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the signature matters.</b> Plain double-submit (compare the cookie value
/// to the header value) breaks if an attacker can plant a cookie on the domain
/// (sibling-subdomain XSS, http MITM): they set a known value and a matching
/// header. The HMAC signature closes that gap — an attacker cannot produce a value
/// that <see cref="Validate"/> accepts without the secret key. This is OWASP's
/// "signed double-submit" and is why ADR-0007 chose it over both naive
/// double-submit and the framework's <c>IAntiforgery</c>.
/// </para>
/// <para>
/// The HMAC key is a DEDICATED <c>Csrf:Key</c> (≥32-char server-only secret),
/// separate from <c>Jwt:Key</c> — key separation keeps the two cryptographic
/// purposes independent (a leak/rotation of the JWT key doesn't force CSRF rotation,
/// and vice versa). Registered as a singleton (immutable after construction).
/// </para>
/// </remarks>
public sealed class CsrfTokenService : ICsrfTokenService
{
    private readonly byte[] _key;

    public CsrfTokenService(IOptions<CsrfOptions> csrfOptions)
    {
        _key = Encoding.UTF8.GetBytes(csrfOptions.Value.Key);
    }

    /// <inheritdoc />
    public string Issue()
    {
        string random = SecureTokens.NewToken(32);
        return $"{random}.{Sign(random)}";
    }

    /// <inheritdoc />
    public bool Validate(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        string[] parts = token.Split('.');
        if (parts.Length != 2)
        {
            return false;
        }

        string expectedSignature = Sign(parts[0]);
        return SecureTokens.FixedTimeEquals(expectedSignature, parts[1]);
    }

    private string Sign(string value)
    {
        byte[] hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value));
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
