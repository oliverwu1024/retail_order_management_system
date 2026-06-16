using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// A line in a <see cref="Cart"/> (DATABASE_DESIGN §3.9): a variant plus a quantity, with
/// the unit price <em>snapshotted at add-time</em>.
/// </summary>
/// <remarks>
/// Snapshotting <see cref="UnitPriceCentsSnapshot"/> keeps the cart total stable while the
/// shopper browses even if the catalogue price changes underneath them; checkout
/// re-reads the live price and warns if it has drifted. A unique index on
/// (CartId, ProductVariantId) prevents duplicate lines — adding the same variant again
/// bumps the existing line's quantity instead.
/// </remarks>
public class CartItem : IAuditableEntity
{
    /// <summary>Surrogate PK.</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the owning <see cref="Cart"/>.</summary>
    public Guid CartId { get; set; }

    /// <summary>Navigation to the owning cart.</summary>
    public Cart? Cart { get; set; }

    /// <summary>FK to the variant being purchased.</summary>
    public Guid ProductVariantId { get; set; }

    /// <summary>Navigation to the variant.</summary>
    public ProductVariant? ProductVariant { get; set; }

    /// <summary>Units of this variant in the cart (must be &gt; 0).</summary>
    public int Quantity { get; set; }

    /// <summary>The variant's <c>PriceCents</c> captured when the line was added. Cents, never decimal.</summary>
    public int UnitPriceCentsSnapshot { get; set; }

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
