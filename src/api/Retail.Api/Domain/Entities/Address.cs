using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// A saved postal address (DATABASE_DESIGN §3.3), owned by a
/// <see cref="CustomerProfile"/>. A customer may have many.
/// </summary>
/// <remarks>
/// <para>
/// "Shipping vs billing" is modelled as two independent boolean flags rather than a
/// discrete type column — one address can be the default for shipping, billing, both,
/// or neither. The invariant "at most one default per axis per profile" is enforced
/// both in the service layer (clear-then-set) AND by a SQL Server filtered unique
/// index (see <c>AddressConfiguration</c>), so the database itself rejects a second
/// default.
/// </para>
/// <para>
/// NOT soft-deletable — only Product/Category/Review use soft delete (DATABASE_DESIGN §1).
/// Address removal is a hard delete.
/// </para>
/// </remarks>
public class Address : IAuditableEntity
{
    /// <summary>Surrogate PK (DB-generated sequential GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the owning <see cref="CustomerProfile"/>.</summary>
    public Guid CustomerProfileId { get; set; }

    /// <summary>Navigation to the owning profile.</summary>
    public CustomerProfile? CustomerProfile { get; set; }

    /// <summary>Street address line 1.</summary>
    public string Line1 { get; set; } = string.Empty;

    /// <summary>Street address line 2 (apartment, suite, etc.). Nullable.</summary>
    public string? Line2 { get; set; }

    /// <summary>City / locality.</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>State / province / region. Nullable.</summary>
    public string? Region { get; set; }

    /// <summary>Postal / ZIP code.</summary>
    public string PostalCode { get; set; } = string.Empty;

    /// <summary>ISO-3166 alpha-2 country code (e.g. "AU", "US"). Stored as <c>char(2)</c>.</summary>
    public string Country { get; set; } = string.Empty;

    /// <summary>Whether this is the profile's default shipping address (at most one true per profile).</summary>
    public bool IsDefaultShipping { get; set; }

    /// <summary>Whether this is the profile's default billing address (at most one true per profile).</summary>
    public bool IsDefaultBilling { get; set; }

    // ── IAuditableEntity (stamped by AuditingInterceptor) ────────────────────
    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }
    /// <inheritdoc />
    public string? CreatedBy { get; set; }
    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }
    /// <inheritdoc />
    public string? UpdatedBy { get; set; }
}
