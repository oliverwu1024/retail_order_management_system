namespace Retail.Api.Identity;

/// <summary>
/// Auth-level switches bound from the <c>Auth</c> configuration section.
/// </summary>
/// <remarks>
/// <see cref="SecureCookies"/> exists because the <c>Secure</c> cookie attribute
/// requires HTTPS: <c>true</c> in production, but settable to <c>false</c> in
/// <c>appsettings.Development.json</c> so the cookies still flow when testing over
/// plain http (e.g. docker-compose). See ADR-0007 "Secure cookies require HTTPS".
/// The default is the safe one (<c>true</c>) so a missing config never silently
/// downgrades production security.
/// </remarks>
public sealed class AuthSettings
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Auth";

    /// <summary>Whether auth cookies carry the <c>Secure</c> flag (HTTPS-only). Defaults to <c>true</c>.</summary>
    public bool SecureCookies { get; set; } = true;
}
