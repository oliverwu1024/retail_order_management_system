namespace Retail.Api.Identity;

/// <summary>
/// Issues and validates signed CSRF tokens for the double-submit-cookie defense
/// (ADR-0007). The token is self-contained: <c>random.HMAC(random)</c>, so
/// validation needs no server-side state — just the secret key.
/// </summary>
public interface ICsrfTokenService
{
    /// <summary>Issues a fresh signed token to place in the (readable) <c>csrf</c> cookie.</summary>
    string Issue();

    /// <summary>
    /// Returns <c>true</c> only if the token is well-formed and its HMAC signature
    /// verifies against the server key — i.e. it was minted by us and not forged.
    /// </summary>
    bool Validate(string? token);
}
