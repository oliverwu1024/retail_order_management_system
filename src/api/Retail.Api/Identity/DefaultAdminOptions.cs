namespace Retail.Api.Identity;

/// <summary>
/// The seeded default-administrator credentials, bound from the
/// <c>Auth:DefaultAdmin</c> configuration section (REQUIREMENTS §1.3).
/// </summary>
/// <remarks>
/// <see cref="Password"/> is a secret and is NOT committed — it is supplied via
/// User Secrets in development (<c>dotnet user-secrets set Auth:DefaultAdmin:Password ...</c>)
/// and Key Vault in production, mirroring how <c>Jwt:Key</c> is handled. If the
/// email or password is blank the seeder skips admin creation and logs a warning
/// rather than seeding a guessable account.
/// </remarks>
public sealed class DefaultAdminOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Auth:DefaultAdmin";

    /// <summary>Admin login email (also used as the username).</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Admin password. Secret — supplied via User Secrets / Key Vault, never source.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Admin display name.</summary>
    public string DisplayName { get; set; } = "Administrator";
}
