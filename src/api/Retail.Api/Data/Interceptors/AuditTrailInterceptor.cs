using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Retail.Api.Common.Abstractions;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data.Interceptors;

// ─────────────────────────────────────────────────────────────────────────────
//  AuditTrailInterceptor — the SECOND SaveChanges interceptor (alongside
//  AuditingInterceptor). Where AuditingInterceptor STAMPS CreatedBy/UpdatedAt
//  columns on the changing row, this one EMITS an immutable AuditLog HISTORY row
//  (actor + before/after JSON) for every Insert/Update/Delete of a MONITORED
//  entity, so admins can answer "who changed this order, and what did it look
//  like before?".
//
//  WHY A SEPARATE INTERCEPTOR (not fold it into AuditingInterceptor)?
//  -----------------------------------------------------------------
//  Two different jobs: stamping MUTATES the same row's columns; trailing APPENDS
//  a different row to a different table. Keeping them apart leaves each
//  single-responsibility (and the existing audit-stamp tests untouched). EF Core
//  composes interceptors, which is exactly what AuditingInterceptor's own comment
//  anticipated.
//
//  WHY CAPTURE-AND-ADD IN SavingChanges (one pass, no post-save second save)?
//  -------------------------------------------------------------------------
//  Every monitored entity has a CLIENT-generated Guid PK, so the EntityId is
//  already known before the SQL runs — no post-save hook needed to learn the key.
//  We snapshot before/after values and ADD the AuditLog rows to the same
//  ChangeTracker, so they INSERT in the same SaveChanges, inside the same
//  transaction as the change they describe (atomic — a rolled-back business change
//  rolls back its audit row too). The only values not yet materialised are
//  DB-generated technical columns (OrderNumber, RowVersion) which we deliberately
//  don't audit. We enumerate the entries into a list BEFORE adding audit rows so
//  we never mutate the collection we're iterating, and AuditLog itself is NOT
//  monitored, so this can't recurse.
//
//  PII: the JSON snapshot redacts sensitive fields (email, address, raw payloads,
//  secrets) and skips binary columns (e.g. RowVersion). AuditLog is admin-readable,
//  and CODING_STANDARDS treats email/PII as never-log data.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// EF Core <see cref="SaveChangesInterceptor"/> that appends an immutable
/// <see cref="AuditLog"/> row (actor + before/after JSON) for every create/update/delete of a
/// monitored domain entity.
/// </summary>
public sealed class AuditTrailInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserAccessor _currentUser;
    private readonly TimeProvider _timeProvider;

    // The entity types whose mutations are recorded (REQUIREMENTS §11.1). Anything not in here
    // (carts, reservations, profiles, AuditLog itself) is NOT trailed.
    private static readonly HashSet<Type> Monitored = new()
    {
        typeof(Product),
        typeof(InventoryItem),
        typeof(Order),
        typeof(Payment),
        typeof(Shipment),
    };

    // Property names that must never reach an admin-readable JSON snapshot — redacted to "***".
    private static readonly HashSet<string> Redacted = new(StringComparer.OrdinalIgnoreCase)
    {
        "GuestEmail", "Email", "Password", "PasswordHash", "Token", "Secret",
        "ShippingAddress", "BillingAddress", "RawPayloadJson",
    };

    public AuditTrailInterceptor(ICurrentUserAccessor currentUser, TimeProvider timeProvider)
    {
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    /// <summary>Sync interception point. Called from <c>DbContext.SaveChanges()</c>.</summary>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <summary>Async interception point. Called from <c>DbContext.SaveChangesAsync()</c>.</summary>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Capture(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string actor = _currentUser.UserId ?? "system";

        // Snapshot the monitored changes into a list FIRST — adding AuditLog rows below mutates
        // the ChangeTracker, so we must not be iterating it at the same time. The explicit
        // AuditLog exclusion is defensive recursion-safety: the trail is an append-only ledger that
        // must never trail itself, even if someone later adds AuditLog to the Monitored set.
        List<EntityEntry> tracked = context.ChangeTracker.Entries()
            .Where(e => Monitored.Contains(e.Metadata.ClrType)
                        && e.Metadata.ClrType != typeof(AuditLog)
                        && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (tracked.Count == 0)
        {
            return;
        }

        var rows = new List<AuditLog>(tracked.Count);
        foreach (EntityEntry entry in tracked)
        {
            rows.Add(new AuditLog
            {
                Actor = actor,
                Action = entry.State switch
                {
                    EntityState.Added => "Insert",
                    EntityState.Deleted => "Delete",
                    _ => "Update",
                },
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = KeyOf(entry),
                // Before = the original DB values (null for a fresh insert).
                BeforeJson = entry.State == EntityState.Added ? null : Serialize(entry.OriginalValues),
                // After = the new values (null for a delete).
                AfterJson = entry.State == EntityState.Deleted ? null : Serialize(entry.CurrentValues),
                OccurredAt = now,
            });
        }

        context.Set<AuditLog>().AddRange(rows);
    }

    // The primary key as a string (all monitored entities have a single Guid PK; composite keys
    // are joined just in case).
    private static string KeyOf(EntityEntry entry)
    {
        IKey? pk = entry.Metadata.FindPrimaryKey();
        if (pk is null)
        {
            return string.Empty;
        }

        IEnumerable<string?> parts = pk.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString());
        return string.Join("|", parts);
    }

    // Serializes a row's scalar property values to JSON, redacting sensitive fields and skipping
    // binary columns (e.g. RowVersion) that would only add noise.
    private static string Serialize(PropertyValues values)
    {
        var bag = new Dictionary<string, object?>();
        foreach (IProperty prop in values.Properties)
        {
            if (prop.ClrType == typeof(byte[]))
            {
                continue;
            }

            bag[prop.Name] = Redacted.Contains(prop.Name) ? "***" : values[prop.Name];
        }

        return JsonSerializer.Serialize(bag);
    }
}
