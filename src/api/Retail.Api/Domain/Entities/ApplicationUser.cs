using Microsoft.AspNetCore.Identity;
using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// Application user entity. Extends ASP.NET Identity's <see cref="IdentityUser"/>
/// to add domain-specific profile fields. The Identity base class provides:
/// Id, UserName, Email, PasswordHash, SecurityStamp, ConcurrencyStamp,
/// PhoneNumber, TwoFactorEnabled, LockoutEnd, AccessFailedCount, and the
/// matching *Confirmed boolean flags.
/// </summary>
/// <remarks>
/// <para>
/// Storage shape: Identity persists this to the AspNetUsers table via
/// AddEntityFrameworkStores&lt;RetailDbContext&gt;() (see Program.cs and RetailDbContext).
/// The string Id is a GUID serialized as text — Identity's default.
/// </para>
/// <para>
/// Implements <see cref="IAuditableEntity"/> so the AuditingInterceptor stamps
/// when (and by whom) each account row was created/updated — for a self-signup
/// <c>CreatedBy</c> is null ("system"), for a staff account created by an admin it
/// records the admin's id. This is the composability payoff the interface was
/// designed for: a class that already inherits <c>IdentityUser</c> still gains
/// audit fields.
/// </para>
/// </remarks>
public class ApplicationUser : IdentityUser, IAuditableEntity
{
    /// <summary>
    /// User-facing display name, collected at registration (REQUIREMENTS §1.1).
    /// Nullable at the storage level so admin-seeded/legacy rows are valid, but the
    /// registration validator requires it for self-signup.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// User's given name. Nullable — set during profile completion (Phase 1.4),
    /// not at registration.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// User's family name. Same nullability rationale as <see cref="FirstName"/>.
    /// </summary>
    public string? LastName { get; set; }

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
