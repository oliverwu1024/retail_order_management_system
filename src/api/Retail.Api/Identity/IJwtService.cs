using Retail.Api.Domain.Entities;

namespace Retail.Api.Identity;

/// <summary>
/// Mints signed access JWTs. Deliberately narrow: it knows nothing about refresh
/// tokens, cookies, or HTTP — just "given a user and their roles, produce a signed
/// access token and tell me when it expires".
/// </summary>
public interface IJwtService
{
    /// <summary>
    /// Creates a signed access token for the user with the given role claims.
    /// </summary>
    /// <returns>The compact-serialized JWT and its absolute expiry.</returns>
    (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(ApplicationUser user, IEnumerable<string> roles);
}
