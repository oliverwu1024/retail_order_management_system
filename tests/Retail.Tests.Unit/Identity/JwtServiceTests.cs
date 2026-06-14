using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Retail.Api.Domain.Entities;
using Retail.Api.Identity;

namespace Retail.Tests.Unit.Identity;

/// <summary>
/// Unit tests for <see cref="JwtService"/> — the access-token minting half of auth.
/// Tokens are minted at a fixed clock and validated with the same signing key /
/// issuer / audience the production JwtBearer pipeline uses, so a passing test
/// means a real request would authenticate.
/// </summary>
public class JwtServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 14, 9, 0, 0, TimeSpan.Zero);
    private const string Key = "unit-test-signing-key-0123456789-abcdefgh"; // ≥32 chars
    private const string Issuer = "https://test-issuer";
    private const string Audience = "test-audience";

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static JwtService NewService(int accessMinutes = 15)
    {
        IOptions<JwtOptions> options = Options.Create(new JwtOptions
        {
            Issuer = Issuer,
            Audience = Audience,
            Key = Key,
            AccessTokenMinutes = accessMinutes,
            RefreshTokenDays = 14,
        });
        return new JwtService(options, new FixedClock(Now));
    }

    // Validates signature + issuer + audience (NOT lifetime — that is asserted
    // directly via the returned expiry, so the test does not depend on the real clock).
    private static ClaimsPrincipal Validate(string token)
    {
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = false,
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
        };
        return new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _);
    }

    [Fact]
    public void CreateAccessToken_EmitsSubEmailAndRoles_InAValidToken()
    {
        // Arrange
        JwtService sut = NewService();
        var user = new ApplicationUser { Id = "user-123", Email = "u@test.local", DisplayName = "Tester" };

        // Act
        (string token, DateTimeOffset expiresAt) = sut.CreateAccessToken(user, new[] { "Customer", "Staff" });
        ClaimsPrincipal principal = Validate(token);

        // Assert — default JwtBearer inbound mapping turns sub→NameIdentifier, role→Role.
        Assert.Equal("user-123", principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("u@test.local", principal.FindFirstValue(ClaimTypes.Email));
        Assert.Contains(principal.Claims, c => c.Type == ClaimTypes.Role && c.Value == "Customer");
        Assert.Contains(principal.Claims, c => c.Type == ClaimTypes.Role && c.Value == "Staff");
    }

    [Fact]
    public void CreateAccessToken_ExpiresAtNowPlusConfiguredMinutes()
    {
        JwtService sut = NewService(accessMinutes: 20);
        var user = new ApplicationUser { Id = "u1", Email = "e@test.local" };

        (_, DateTimeOffset expiresAt) = sut.CreateAccessToken(user, Array.Empty<string>());

        Assert.Equal(Now.AddMinutes(20), expiresAt);
    }

    [Fact]
    public void CreateAccessToken_SignatureDoesNotVerifyUnderADifferentKey()
    {
        JwtService sut = NewService();
        var user = new ApplicationUser { Id = "u1", Email = "e@test.local" };
        (string token, _) = sut.CreateAccessToken(user, Array.Empty<string>());

        var wrongKeyParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-different-key-0123456789-abcdefghij")),
        };

        Assert.ThrowsAny<SecurityTokenException>(
            () => new JwtSecurityTokenHandler().ValidateToken(token, wrongKeyParameters, out _));
    }
}
