using Microsoft.AspNetCore.Identity;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// Application user entity. Extends ASP.NET Identity's <see cref="IdentityUser"/>
/// to add domain-specific profile fields. The Identity base class provides:
/// Id, UserName, Email, PasswordHash, SecurityStamp, ConcurrencyStamp,
/// PhoneNumber, TwoFactorEnabled, LockoutEnd, AccessFailedCount, and the
/// matching *Confirmed boolean flags.
/// </summary>
/// <remarks>
/// Storage shape: Identity persists this to the AspNetUsers table via
/// AddEntityFrameworkStores&lt;RetailDbContext&gt;() (see Program.cs and RetailDbContext).
/// The string Id is a GUID serialized as text — Identity's default.
/// </remarks>
public class ApplicationUser : IdentityUser
{
    /// <summary>
    /// User's given name. Nullable for now (set during profile completion,
    /// not strictly required at registration). Will become required once
    /// the registration flow lands in Phase 1.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// User's family name. Same nullability rationale as <see cref="FirstName"/>.
    /// </summary>
    public string? LastName { get; set; }
}
