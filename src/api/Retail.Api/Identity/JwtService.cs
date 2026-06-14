using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Identity;

/// <summary>
/// <see cref="IJwtService"/> backed by <see cref="JwtSecurityTokenHandler"/> with
/// HMAC-SHA256 signing.
/// </summary>
/// <remarks>
/// <para>
/// <b>Claim choices and the round-trip with JwtBearer.</b> The user id is emitted
/// as the standard <c>sub</c> claim. With JwtBearer's default inbound claim
/// mapping (on, in <c>Program.cs</c>), <c>sub</c> is mapped to
/// <see cref="ClaimTypes.NameIdentifier"/> on the validated principal — which is
/// exactly what <c>HttpContextCurrentUserAccessor</c> (and the audit pipeline)
/// reads. Roles are emitted as <see cref="ClaimTypes.Role"/> so the default
/// <c>RoleClaimType</c> makes <c>[Authorize(Roles = ...)]</c> work without extra
/// configuration.
/// </para>
/// <para>
/// <b>Lifetime.</b> Registered as a singleton — it is immutable after construction
/// (the signing key and handler are fixed) and <see cref="JwtSecurityTokenHandler"/>
/// token creation is thread-safe. The clock comes from <see cref="TimeProvider"/>
/// so tests can mint tokens at a fixed instant.
/// </para>
/// </remarks>
public sealed class JwtService : IJwtService
{
    private readonly JwtOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SigningCredentials _signingCredentials;
    private readonly JwtSecurityTokenHandler _handler = new();

    public JwtService(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;

        byte[] keyBytes = Encoding.UTF8.GetBytes(_options.Key);
        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(keyBytes),
            SecurityAlgorithms.HmacSha256);
    }

    /// <inheritdoc />
    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(ApplicationUser user, IEnumerable<string> roles)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset expiresAt = now.AddMinutes(_options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            // sub → NameIdentifier (the audit pipeline + UserManager.GetUserId read this).
            new(JwtRegisteredClaimNames.Sub, user.Id),
            // jti = unique token id, so two tokens minted in the same second still differ.
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Name, user.DisplayName ?? user.Email ?? user.Id),
        };

        foreach (string role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: _signingCredentials);

        return (_handler.WriteToken(token), expiresAt);
    }
}
