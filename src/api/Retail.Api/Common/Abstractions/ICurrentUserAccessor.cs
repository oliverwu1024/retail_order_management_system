namespace Retail.Api.Common.Abstractions;

// ─────────────────────────────────────────────────────────────────────────────
//  ICurrentUserAccessor — the single piece of request context the audit pipeline
//  needs: "who is the current user?". It is the seam that lets the data layer
//  (AuditingInterceptor) stamp CreatedBy/UpdatedBy WITHOUT depending on ASP.NET's
//  HttpContext.
//
//  WHY THIS EXISTS (instead of the interceptor reading IHttpContextAccessor):
//  -------------------------------------------------------------------------
//  1. Layering. An EF Core interceptor is data-layer infrastructure; it should
//     not reach up into the web layer (HttpContext). This interface inverts that
//     dependency — the HTTP detail lives in ONE adapter (HttpContextCurrentUserAccessor),
//     and the interceptor depends only on the abstract notion of a user id.
//  2. Testability. Unit-testing the interceptor needs only a one-line fake of
//     this interface — no ASP.NET framework dragged into the test project.
//  3. Reuse. Background workers, seeders, or a future gRPC/host can register a
//     different implementation (e.g. a fixed "system" principal) without the
//     interceptor changing at all.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Resolves the identity of the caller behind the current unit of work.
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>
    /// The Identity user id of the current principal, or <c>null</c> when there
    /// is no authenticated user — an anonymous request, a background worker, or
    /// seed/migration-time work. A null here means "system" actor, which is the
    /// truthful audit value for those paths.
    /// </summary>
    string? UserId { get; }
}
