using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// A restock recommendation for a variant (DATABASE_DESIGN §3.18) — Phase 5B (forecasting).
/// </summary>
/// <remarks>
/// <para>
/// SYSTEM-GENERATED, ONE UPSERTED ROW PER VARIANT. The daily refresh computes
/// <c>RecommendedOrderQty = max(0, forecast₁₄d + safetyStock − onHand)</c> (safetyStock =
/// <c>z · σ · √leadTimeDays</c>) and upserts the single row for the variant (PHASE_5B_FORECAST_SCOPE §3.5).
/// The back-office Reorder list shows rows where <c>!Dismissed &amp;&amp; RecommendedOrderQty &gt; 0</c>,
/// ranked by quantity.
/// </para>
/// <para>
/// DISMISS STICKS. A Staff/StoreManager dismisses a hint (<see cref="Dismissed"/> = true) to clear it
/// from the list; the next refresh updates the quantity/reasoning but leaves it dismissed, so Dismiss
/// is meaningful. There is no dedicated who-dismissed column — the dismiss is the only mutation, so the
/// <see cref="IAuditableEntity"/> <c>UpdatedBy</c>/<c>UpdatedAt</c> stamps record the actor and time.
/// </para>
/// <para>
/// NOT AUDIT-MONITORED (high-volume, system-generated); the <see cref="IAuditableEntity"/> stamps still
/// apply via <c>AuditingInterceptor</c>.
/// </para>
/// </remarks>
public class ReorderHint : IAuditableEntity
{
    /// <summary>Surrogate PK (EF-generated sequential GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the variant to restock. Required.</summary>
    public Guid ProductVariantId { get; set; }

    /// <summary>Navigation to the variant.</summary>
    public ProductVariant ProductVariant { get; set; } = null!;

    /// <summary>Recommended units to order (≥ 0).</summary>
    public int RecommendedOrderQty { get; set; }

    /// <summary>Human-readable basis, e.g. "14-day demand 84 + safety 22, on-hand 30". ≤ 400 chars.</summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>When this hint was last computed (UTC), service-stamped via <c>TimeProvider</c>.</summary>
    public DateTimeOffset GeneratedAt { get; set; }

    /// <summary>Cleared by a Staff/StoreManager from the Reorder list; sticks across refreshes.</summary>
    public bool Dismissed { get; set; }

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
