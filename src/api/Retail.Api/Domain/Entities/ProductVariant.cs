using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// A purchasable variant of a <see cref="Product"/> — the unit that actually
/// carries price, options, and (1:1) stock (DATABASE_DESIGN §3.6). Deactivated via
/// <see cref="IsActive"/> rather than soft-deleted.
/// </summary>
public class ProductVariant : IAuditableEntity
{
    /// <summary>Surrogate PK (DB-generated sequential GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the owning <see cref="Product"/>.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Navigation to the product.</summary>
    public Product? Product { get; set; }

    /// <summary>Stock-keeping unit; unique globally.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>
    /// Free-form variant options (e.g. <c>{ "size": "M", "color": "red" }</c>).
    /// Persisted as the <c>OptionsJson</c> column via a JSON ValueConverter.
    /// </summary>
    public Dictionary<string, string> Options { get; set; } = new();

    /// <summary>Price in integer cents (≥ 0). Money is never <c>decimal</c> on hot tables.</summary>
    public int PriceCents { get; set; }

    /// <summary>Optional "was" price in cents for strikethrough display.</summary>
    public int? CompareAtPriceCents { get; set; }

    /// <summary>Whether the variant is sellable.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>1:1 stock record for this variant.</summary>
    public InventoryItem? Inventory { get; set; }

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
