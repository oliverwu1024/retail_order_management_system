using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// A customer's domain profile (DATABASE_DESIGN §3.2) — 1:1 with an
/// <see cref="ApplicationUser"/> that holds the <c>Customer</c> role.
/// </summary>
/// <remarks>
/// <para>
/// WHY A SEPARATE ENTITY INSTEAD OF MORE FIELDS ON <see cref="ApplicationUser"/>?
/// ASP.NET Identity's user owns <em>authentication</em> concerns (email, password,
/// security stamps, lockout, 2FA phone). A customer's <em>domain</em> data — their
/// preferred display name, a plain contact phone, and their saved addresses — lives
/// here so the two concerns stay decoupled. In particular this keeps a contact phone
/// off Identity's inherited <c>PhoneNumber</c>, which carries 2FA/confirmation
/// semantics we don't want to entangle with "where do we ship to".
/// </para>
/// <para>
/// LIFECYCLE: profiles are created lazily — a customer gets one the first time they
/// open "My Account" (GET profile), seeded from the display name captured at
/// registration. Staff / StoreManager / Administrator accounts never get a profile.
/// </para>
/// <para>
/// <see cref="DisplayName"/> is the canonical, editable value; it is mirrored back to
/// <see cref="ApplicationUser.DisplayName"/> on save so the lightweight
/// <c>/auth/me</c> session path stays one cheap query (no profile load on every login).
/// </para>
/// </remarks>
public class CustomerProfile : IAuditableEntity
{
    /// <summary>Surrogate PK (DB-generated sequential GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// FK to the owning Identity user (<c>AspNetUsers.Id</c>). A <c>string</c>, not a
    /// <c>Guid</c>, because Identity stores its Id as a GUID serialized to text
    /// (<c>nvarchar(450)</c>). Unique — this is what enforces the 1:1 with the user.
    /// </summary>
    public string AppUserId { get; set; } = string.Empty;

    /// <summary>Navigation to the owning Identity user.</summary>
    public ApplicationUser? User { get; set; }

    /// <summary>User-facing display name. Editable; mirrored to <see cref="ApplicationUser.DisplayName"/> on save.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Contact phone (E.164 ideally). Nullable — not collected at registration.</summary>
    public string? Phone { get; set; }

    /// <summary>The customer's saved shipping/billing addresses.</summary>
    public ICollection<Address> Addresses { get; set; } = new List<Address>();

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
