namespace Retail.Api.Identity;

/// <summary>
/// Strongly-typed view of the <c>Jwt</c> configuration section. Bound in
/// <c>Program.cs</c> via <c>Configure&lt;JwtOptions&gt;</c> and injected as
/// <c>IOptions&lt;JwtOptions&gt;</c> wherever token settings are needed.
/// </summary>
/// <remarks>
/// Replaces the scattered <c>builder.Configuration["Jwt:Key"]</c> string indexing
/// that was in <c>Program.cs</c> — one typed object is easier to validate, inject,
/// and fake in tests. <see cref="Key"/> comes from User Secrets / Key Vault, never
/// source (see <c>appsettings.json</c> comment and ADR-0007).
/// </remarks>
public sealed class JwtOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Jwt";

    /// <summary>Token issuer (<c>iss</c>) — validated on every request.</summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>Token audience (<c>aud</c>) — validated on every request.</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 signing key (≥32 chars). Secret — never committed.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Access-token lifetime in minutes. Short by design (PRD §1.1 = 15).</summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>Refresh-token lifetime in days (PRD §1.1 = 14).</summary>
    public int RefreshTokenDays { get; set; } = 14;
}
