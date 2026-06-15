namespace Retail.Api.Identity;

/// <summary>
/// Strongly-typed view of the <c>Csrf</c> configuration section. Bound in
/// <c>Program.cs</c> and injected as <c>IOptions&lt;CsrfOptions&gt;</c> by
/// <see cref="CsrfTokenService"/>.
/// </summary>
/// <remarks>
/// A DEDICATED signing key, separate from <c>Jwt:Key</c> — key separation so the two
/// HMAC purposes (a token the server signs for itself vs. a token the client echoes
/// back) can be rotated independently, and a leak/rotation of one never forces the
/// other. <see cref="Key"/> comes from User Secrets / Key Vault, never source.
/// </remarks>
public sealed class CsrfOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Csrf";

    /// <summary>HMAC-SHA256 signing key for CSRF tokens (≥32 chars). Secret — never committed.</summary>
    public string Key { get; set; } = string.Empty;
}
