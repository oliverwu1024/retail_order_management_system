using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// A flagged-for-review marker on an <see cref="Order"/> (DATABASE_DESIGN §3.19) — Phase 5B.
/// </summary>
/// <remarks>
/// <para>
/// SYSTEM-GENERATED, ONE PER FLAGGED ORDER. Written by the order-anomaly scan
/// (<c>OrderAnomalyService</c>, Phase-5B Chunk 2) when an order trips any of the three
/// detection rules (Z-score on the buyer's recent totals, a never-seen shipping country,
/// or a quantity spike — REQUIREMENTS §10.1). Exactly one row is written per flagged
/// order: <see cref="Reason"/> carries the combined human-readable cause(s) and
/// <see cref="Score"/> the rule-1 Z-score (or <c>0</c> when only the country/quantity
/// rules fire). The scan is idempotent — it never re-flags an order that already has a row.
/// </para>
/// <para>
/// ACKNOWLEDGE GATES SHIPPING. While <see cref="Acknowledged"/> is false the order is
/// blocked from "Mark Shipped" (REQUIREMENTS §10.2); a Staff/StoreManager clears it from
/// the Risk Queue, after which the order ships normally. There is no dedicated
/// who-acknowledged column — the acknowledge mutation is the only update an anomaly row
/// ever receives, so the <see cref="IAuditableEntity"/> <c>UpdatedBy</c>/<c>UpdatedAt</c>
/// stamps already record the actor and time.
/// </para>
/// <para>
/// NOT AUDIT-MONITORED. Like <see cref="Review"/>, anomaly rows are deliberately kept OFF
/// the <c>AuditTrailInterceptor</c> allowlist (high volume, system-generated). The
/// <see cref="IAuditableEntity"/> column stamps still apply via <c>AuditingInterceptor</c>.
/// </para>
/// </remarks>
public class OrderAnomaly : IAuditableEntity
{
    /// <summary>Surrogate PK (EF-generated sequential GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the flagged <see cref="Order"/>. Required.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Navigation to the flagged order.</summary>
    public Order Order { get; set; } = null!;

    /// <summary>
    /// The rule-1 Z-score (<c>|Z|</c> of the order total against the buyer's baseline), or
    /// <c>0</c> when only the new-country / quantity rules fired. <c>decimal(8,3)</c> in SQL.
    /// </summary>
    public decimal Score { get; set; }

    /// <summary>
    /// Human-readable cause(s), e.g. "Order total 4.2σ above this customer's mean; ships to a
    /// country not seen on prior orders". ≤ 200 chars.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>When the scan flagged this order (UTC), service-stamped via <c>TimeProvider</c>.</summary>
    public DateTimeOffset DetectedAt { get; set; }

    /// <summary>
    /// Cleared by a Staff/StoreManager from the Risk Queue. While false the order can't be
    /// marked shipped (REQUIREMENTS §10.2).
    /// </summary>
    public bool Acknowledged { get; set; }

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
