using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// A line item on a placed <see cref="Order"/> (DATABASE_DESIGN §3.12).
/// </summary>
/// <remarks>
/// Carries SKU / name / price <em>snapshots</em> so a historical order renders correctly
/// even after the variant is renamed, repriced, or deactivated. <see cref="LineTotalCents"/>
/// is stored (not computed) so the line total is frozen at placement and never recomputes
/// against a since-changed price. The FK to the variant is kept (and configured
/// <c>Restrict</c>) so the variant can't be hard-deleted out from under order history.
/// </remarks>
public class OrderLine : IAuditableEntity
{
    /// <summary>Surrogate PK.</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the owning <see cref="Order"/>.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Navigation to the owning order.</summary>
    public Order? Order { get; set; }

    /// <summary>FK to the purchased variant (kept for traceability; not used for display — snapshots are).</summary>
    public Guid ProductVariantId { get; set; }

    /// <summary>Navigation to the variant.</summary>
    public ProductVariant? ProductVariant { get; set; }

    /// <summary>Units purchased.</summary>
    public int Quantity { get; set; }

    /// <summary>Unit price in cents at the moment of commit.</summary>
    public int UnitPriceCents { get; set; }

    /// <summary>Stored line total in cents (= <see cref="UnitPriceCents"/> × <see cref="Quantity"/>).</summary>
    public int LineTotalCents { get; set; }

    /// <summary>Variant SKU as it was at purchase.</summary>
    public string SkuSnapshot { get; set; } = string.Empty;

    /// <summary>Product/variant display name as it was at purchase.</summary>
    public string NameSnapshot { get; set; } = string.Empty;

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
