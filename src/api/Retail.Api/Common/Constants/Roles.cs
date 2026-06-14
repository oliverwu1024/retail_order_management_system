namespace Retail.Api.Common.Constants;

/// <summary>
/// Canonical role names — the single source of truth for the four RBAC roles.
/// </summary>
/// <remarks>
/// <para>
/// These constants are referenced in three places that MUST agree: the seeder
/// (<c>IdentityDataSeeder</c>) that creates the roles, the <c>[Authorize(Roles = ...)]</c>
/// attributes on controllers, and the role assignment at registration. Using
/// string literals in those places instead would risk a silent typo
/// ("Adminstrator") that compiles fine and then fails authorization at runtime.
/// </para>
/// <para>
/// The role hierarchy (widest to narrowest authority) is
/// <see cref="Administrator"/> ⊃ <see cref="StoreManager"/> ⊃ <see cref="Staff"/>,
/// with <see cref="Customer"/> as the storefront-only role. The full permission
/// matrix lands in Phase 3 (REQUIREMENTS §1.3); Phase 1 only needs the names to
/// exist and be seeded.
/// </para>
/// </remarks>
public static class Roles
{
    /// <summary>Storefront shopper. Assigned automatically on self-signup.</summary>
    public const string Customer = "Customer";

    /// <summary>Fulfilment / inventory operator. Narrowest admin role.</summary>
    public const string Staff = "Staff";

    /// <summary>Everything Staff can do, plus user management, refunds, and reports.</summary>
    public const string StoreManager = "StoreManager";

    /// <summary>Full authority. The seeded default admin holds this role.</summary>
    public const string Administrator = "Administrator";

    /// <summary>
    /// All four role names — iterated by the seeder to ensure each exists.
    /// Order is widest-storefront-first; it has no authorization meaning.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Customer,
        Staff,
        StoreManager,
        Administrator,
    };
}
