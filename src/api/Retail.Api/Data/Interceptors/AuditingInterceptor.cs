using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Retail.Api.Domain.Common;

namespace Retail.Api.Data.Interceptors;

// ─────────────────────────────────────────────────────────────────────────────
//  AuditingInterceptor — EF Core SaveChangesInterceptor that auto-populates
//  CreatedAt/CreatedBy/UpdatedAt/UpdatedBy on any entity implementing
//  IAuditableEntity, every time SaveChanges runs.
//
//  WHY AN INTERCEPTOR INSTEAD OF SETTING THESE FIELDS IN EACH SERVICE?
//  ------------------------------------------------------------------
//  1. DRY. Every Add/Update across 30+ entities would otherwise have to
//     remember to stamp the fields. One missed call = a row with no audit
//     trail = a forensic dead end.
//  2. Single source of truth. The clock (UtcNow) and the "current user"
//     resolution happen in exactly one place. We can swap in TimeProvider
//     for testing or a fake user resolver without touching service code.
//  3. Catches mass updates. Bulk updates from controllers, hosted services,
//     migrations, and seeders all flow through SaveChanges — the interceptor
//     stamps them uniformly without each caller knowing about audit.
//  4. Composes with other interceptors. EF Core lets us stack interceptors;
//     this one handles audit, a future OutboxInterceptor will handle outbox
//     enqueue, an OptimisticConcurrencyInterceptor handles row-version, etc.
//
//  WHY OVERRIDE BOTH SavingChanges AND SavingChangesAsync?
//  -------------------------------------------------------
//  EF Core calls the sync version from sync `SaveChanges()` and the async
//  version from `SaveChangesAsync()`. They're separate methods on the
//  interceptor — the async one is NOT just an awaitable wrapper around the
//  sync one. We override both and route them to a common Stamp() helper.
//  Forgetting one would mean half our paths skip audit silently.
//
//  WHY DOES THE INTERCEPTOR HAVE TO BE TRANSIENT/SCOPED?
//  -----------------------------------------------------
//  IHttpContextAccessor is registered scoped. If we made AuditingInterceptor
//  a singleton and captured the accessor at construction, the User would be
//  stale forever. We register the interceptor as Scoped and resolve it per
//  request via DI in Program.cs.
//
//  WHY DO WE READ User.FindFirst(ClaimTypes.NameIdentifier)?
//  ---------------------------------------------------------
//  That's the claim ASP.NET Identity puts the user's Id into by default.
//  An authenticated request's `HttpContext.User` is a ClaimsPrincipal whose
//  identity is built from the JWT (or cookie). The "NameIdentifier" claim
//  is what `userManager.GetUserId()` reads under the hood — so we get the
//  same value either way, without taking a dependency on UserManager (which
//  drags in a DB call we don't need).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// EF Core <see cref="SaveChangesInterceptor"/> that stamps
/// <see cref="IAuditableEntity"/> rows with CreatedAt/CreatedBy on insert
/// and UpdatedAt/UpdatedBy on update.
/// </summary>
public class AuditingInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Constructor uses DI to get the request context (for the current user)
    /// and a <see cref="TimeProvider"/> (so tests can substitute a fake clock).
    /// </summary>
    public AuditingInterceptor(
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Sync interception point. Called from <c>DbContext.SaveChanges()</c>.
    /// </summary>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <summary>
    /// Async interception point. Called from <c>DbContext.SaveChangesAsync()</c>.
    /// </summary>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Walks the ChangeTracker for any entity implementing
    /// <see cref="IAuditableEntity"/> and stamps the audit fields based on
    /// the entry's <see cref="EntityState"/>.
    /// </summary>
    private void Stamp(DbContext? context)
    {
        // Defensive null check — interceptor can theoretically be invoked
        // with a null DbContext (rare, but the EF Core API allows it).
        if (context is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var userId = ResolveCurrentUserId();

        // ChangeTracker.Entries<T>() filters entries to those whose entity
        // type implements T. EF Core handles inheritance correctly — an
        // entity implementing IAuditableEntity via a base class still
        // matches here.
        foreach (EntityEntry<IAuditableEntity> entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // First insert. Stamp both the created fields.
                    // Do NOT touch UpdatedAt/UpdatedBy — they stay null
                    // until the row is actually updated.
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = userId;
                    break;

                case EntityState.Modified:
                    // Existing row being updated. Stamp the updated fields.
                    // We must NOT overwrite CreatedAt/CreatedBy here — those
                    // are immutable after insert. EF's change tracker would
                    // still try to send CreatedAt to the DB if we set it; we
                    // protect it by marking it as IsModified=false below.
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userId;

                    // Belt-and-braces: explicitly mark the created fields as
                    // unmodified so EF doesn't include them in the UPDATE
                    // statement. Without this, a buggy caller that set
                    // CreatedAt = something would silently overwrite the
                    // original insert timestamp.
                    entry.Property(nameof(IAuditableEntity.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditableEntity.CreatedBy)).IsModified = false;
                    break;

                    // Other states (Deleted, Unchanged, Detached) need no
                    // audit handling — we keep the existing audit trail
                    // intact even on soft-delete (the audit fields stay as
                    // they were on the last successful update).
            }
        }
    }

    /// <summary>
    /// Pulls the Identity user Id from the current request's
    /// <see cref="ClaimsPrincipal"/>. Returns null for unauthenticated
    /// requests, background workers without a principal, or seed-time work.
    /// </summary>
    private string? ResolveCurrentUserId()
    {
        // HttpContext is null when SaveChanges runs outside a request — e.g.,
        // background hosted services, migration runners, integration tests.
        // Returning null here is correct: those audit rows show as "system"
        // edits, which is exactly the truth.
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        // ClaimTypes.NameIdentifier is where ASP.NET Identity stores the
        // user's Id. This is the same claim UserManager.GetUserId() reads,
        // but we avoid taking a dependency on UserManager (which would mean
        // pulling a generic + scoping the interceptor more tightly).
        return user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }
}
