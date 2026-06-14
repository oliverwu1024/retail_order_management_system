using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// Stock for a <see cref="ProductVariant"/> (1:1) — DATABASE_DESIGN §3.7. A hot,
/// concurrency-sensitive table: updates are guarded by <see cref="RowVersion"/>
/// optimistic concurrency (wired in Phase 2's reservation/checkout paths).
/// </summary>
public class InventoryItem : IAuditableEntity
{
    /// <summary>Surrogate PK (DB-generated sequential GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the variant this stock belongs to (unique — 1:1).</summary>
    public Guid ProductVariantId { get; set; }

    /// <summary>Navigation to the variant.</summary>
    public ProductVariant? Variant { get; set; }

    /// <summary>Units physically in stock.</summary>
    public int OnHand { get; set; }

    /// <summary>Units held by active reservations.</summary>
    public int Reserved { get; set; }

    /// <summary>
    /// Sellable stock — computed, NEVER stored (DATABASE_DESIGN §3.7). Mapped as
    /// ignored in the EF configuration.
    /// </summary>
    public int Available => OnHand - Reserved;

    /// <summary>
    /// SQL Server <c>rowversion</c> for optimistic concurrency. EF compares it on
    /// UPDATE; a mismatch (another writer won) yields 0 rows → concurrency conflict.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

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
