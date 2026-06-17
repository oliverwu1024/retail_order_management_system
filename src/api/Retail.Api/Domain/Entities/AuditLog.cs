namespace Retail.Api.Domain.Entities;

/// <summary>
/// An immutable audit-trail row (DATABASE_DESIGN §3.16) — the "who did what, when, and what
/// changed" record for every admin-facing mutation. New in Phase 3 (docs/PHASE_3_SCOPE.md §3.2).
/// </summary>
/// <remarks>
/// <para>
/// WRITTEN TWO WAYS: <c>AuditTrailInterceptor</c> auto-emits a row for every Insert/Update/
/// Delete of a MONITORED entity (Product/InventoryItem/Order/Payment/Shipment) with before/
/// after JSON; and business services (refund, ship, inventory-adjust) also write an explicit
/// named-action row (e.g. <c>Action = "Refund"</c>) via <c>IAuditWriter</c> so the
/// human-meaningful event is legible in the viewer rather than reconstructed from a status diff.
/// </para>
/// <para>
/// A TECHNICAL, append-only log — deliberately NOT <c>IAuditableEntity</c> (it has no
/// "who updated this audit row" story) and it uses a <c>bigint</c> IDENTITY PK like
/// <see cref="ProcessedStripeEvent"/>: a narrow monotonic clustered key keeps an
/// ever-growing log from fragmenting the way a random-GUID clustered key would.
/// </para>
/// </remarks>
public class AuditLog
{
    /// <summary>Auto-increment PK (<c>bigint</c> identity) — a narrow clustered key for an append-only log.</summary>
    public long Id { get; set; }

    /// <summary>Identity user id of the actor, or <c>"system"</c> for unauthenticated/background work.</summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>What happened — a CRUD verb (<c>Insert</c>/<c>Update</c>/<c>Delete</c>) or a business action (<c>Refund</c>/<c>Shipped</c>/<c>InventoryAdjusted</c>).</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>The CLR type name of the affected entity (e.g. <c>Order</c>).</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>The affected entity's primary key, as a string.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>JSON snapshot of the entity's values BEFORE the change (PII-redacted). Null on inserts.</summary>
    public string? BeforeJson { get; set; }

    /// <summary>JSON snapshot of the entity's values AFTER the change (PII-redacted). Null on deletes.</summary>
    public string? AfterJson { get; set; }

    /// <summary>When the change occurred (UTC). Set from the injected clock (not a DB default) so tests are deterministic.</summary>
    public DateTimeOffset OccurredAt { get; set; }
}
