namespace Retail.Api.Domain.Common;

// ─────────────────────────────────────────────────────────────────────────────
//  IAuditableEntity — marker interface for any domain entity that should have
//  its CreatedAt/CreatedBy/UpdatedAt/UpdatedBy fields populated automatically
//  by AuditingInterceptor on SaveChanges.
//
//  WHY A MARKER INTERFACE INSTEAD OF AN ABSTRACT BaseEntity CLASS?
//  ---------------------------------------------------------------
//  C# only allows single inheritance. Some entities will inherit from
//  framework base classes (e.g. ApplicationUser : IdentityUser). If audit
//  fields lived on an abstract BaseEntity, ApplicationUser couldn't have
//  audit fields without giving up the Identity base class. An interface is
//  composable: ApplicationUser can be `: IdentityUser, IAuditableEntity`
//  and gain audit fields without affecting its identity behavior.
//
//  WHY THESE FOUR FIELDS?
//  ----------------------
//  * CreatedAt / CreatedBy  — set ONCE on insert; never updated. The "who
//                             first put this row in" answer for forensics.
//  * UpdatedAt / UpdatedBy  — set on every UPDATE. The "who touched this last"
//                             answer. Nullable because the first insert has
//                             no update yet.
//
//  We use DateTimeOffset (not DateTime) so the timestamp carries UTC offset
//  information explicitly. DateTime is ambiguous — "did this row write at
//  3:14 PM local or 3:14 PM UTC?" DateTimeOffset answers that on every read.
//
//  We use string (not Guid) for CreatedBy/UpdatedBy because ASP.NET Identity's
//  user Id is a string (a GUID serialized as text by default). Using string
//  here means the audit fields can store the user Id directly without a
//  conversion at the interceptor seam.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Marker interface implemented by domain entities whose audit fields
/// (CreatedAt/CreatedBy/UpdatedAt/UpdatedBy) should be populated automatically
/// on SaveChanges by <c>AuditingInterceptor</c>.
/// </summary>
public interface IAuditableEntity
{
    /// <summary>UTC timestamp when the row was inserted. Set once, never updated.</summary>
    DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Identity user Id of whoever inserted the row. Null when the insert
    /// happened from an unauthenticated context (e.g. seed data, background
    /// worker without a user principal).
    /// </summary>
    string? CreatedBy { get; set; }

    /// <summary>UTC timestamp of the most recent UPDATE. Null on rows that have never been updated since insert.</summary>
    DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Identity user Id of whoever performed the most recent UPDATE. Null on never-updated rows or anonymous updates.</summary>
    string? UpdatedBy { get; set; }
}
