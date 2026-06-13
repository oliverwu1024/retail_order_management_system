using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Retail.Api.Common.Abstractions;
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
//  2. Single source of truth. The clock (UtcNow) and the current-user
//     resolution happen in exactly one place.
//  3. Catches mass updates. Controllers, hosted services, migrations, and
//     seeders all flow through SaveChanges — the interceptor stamps them
//     uniformly without each caller knowing about audit.
//  4. Composes. EF Core lets us stack interceptors; this one handles audit, a
//     future OutboxInterceptor handles outbox enqueue, and so on.
//
//  WHY OVERRIDE BOTH SavingChanges AND SavingChangesAsync?
//  -------------------------------------------------------
//  EF Core calls the sync version from sync SaveChanges() and the async version
//  from SaveChangesAsync(). They are separate methods — the async one is NOT a
//  wrapper around the sync one. We override both and route to a common Stamp().
//  Forgetting one would mean half our paths skip audit silently. (The unit
//  tests cover both paths for exactly this reason.)
//
//  WHY DEPEND ON ICurrentUserAccessor INSTEAD OF IHttpContextAccessor?
//  -------------------------------------------------------------------
//  An EF interceptor is data-layer infrastructure; it should not reach up into
//  the web layer to read HttpContext. ICurrentUserAccessor is the seam — the
//  HTTP detail lives in one adapter (HttpContextCurrentUserAccessor) and the
//  interceptor depends only on the abstract "current user id". This keeps the
//  layering clean AND makes the interceptor unit-testable with a one-line fake,
//  no ASP.NET framework required in the test project.
//
//  WHY IS THE INTERCEPTOR SCOPED?
//  ------------------------------
//  It depends on ICurrentUserAccessor, whose default implementation wraps the
//  request-scoped IHttpContextAccessor. A singleton interceptor would capture a
//  stale accessor. We register it Scoped and resolve it per request in Program.cs.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// EF Core <see cref="SaveChangesInterceptor"/> that stamps
/// <see cref="IAuditableEntity"/> rows with CreatedAt/CreatedBy on insert and
/// UpdatedAt/UpdatedBy on update.
/// </summary>
public class AuditingInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// DI supplies the current-user accessor (for CreatedBy/UpdatedBy) and a
    /// <see cref="TimeProvider"/> (so tests can substitute a fixed clock).
    /// </summary>
    public AuditingInterceptor(
        ICurrentUserAccessor currentUser,
        TimeProvider timeProvider)
    {
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    /// <summary>Sync interception point. Called from <c>DbContext.SaveChanges()</c>.</summary>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <summary>Async interception point. Called from <c>DbContext.SaveChangesAsync()</c>.</summary>
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
    /// <see cref="IAuditableEntity"/> and stamps the audit fields based on the
    /// entry's <see cref="EntityState"/>.
    /// </summary>
    private void Stamp(DbContext? context)
    {
        // Defensive null check — EF Core's API permits a null Context here (rare).
        if (context is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var userId = _currentUser.UserId;

        // Entries<T>() filters to entries whose entity type implements T. EF Core
        // handles inheritance — an entity implementing IAuditableEntity via a base
        // type still matches.
        foreach (EntityEntry<IAuditableEntity> entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // First insert. Stamp the created fields; leave Updated* null
                    // until the row is actually updated.
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = userId;
                    break;

                case EntityState.Modified:
                    // Existing row update. Stamp the updated fields...
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = userId;

                    // ...then protect the created fields. Marking them IsModified=false
                    // keeps them out of the generated UPDATE, so a buggy caller that
                    // re-sets CreatedAt cannot overwrite the original insert stamp.
                    entry.Property(nameof(IAuditableEntity.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IAuditableEntity.CreatedBy)).IsModified = false;
                    break;

                    // Deleted/Unchanged/Detached need no audit handling — the
                    // existing trail stays intact (including on soft-delete).
            }
        }
    }
}
