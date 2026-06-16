using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// One image in a <see cref="Product"/>'s gallery (PRODUCT_IMAGES_SCOPE). An image is either
/// GENERAL (<see cref="ProductVariantId"/> null — shown for the whole product) or tied to a
/// specific <see cref="ProductVariant"/> (the storefront swaps the gallery on variant select).
/// Exactly one image per product is the <see cref="IsPrimary"/> hero (used by list/cart cards via
/// the denormalized <see cref="Product.PrimaryImageBlobKey"/> cache). Hard-deleted (row + blob).
/// </summary>
public class ProductImage : IAuditableEntity
{
    /// <summary>Surrogate PK (DB-generated sequential GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the owning <see cref="Product"/>.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Navigation to the product.</summary>
    public Product? Product { get; set; }

    /// <summary>
    /// Optional FK to the variant this image is specific to. <c>null</c> = a general product
    /// image shown for every variant.
    /// </summary>
    public Guid? ProductVariantId { get; set; }

    /// <summary>Navigation to the variant (when variant-specific).</summary>
    public ProductVariant? ProductVariant { get; set; }

    /// <summary>Blob key (path) in the <c>product-images</c> container.</summary>
    public string BlobKey { get; set; } = string.Empty;

    /// <summary>Alt text for accessibility (<c>&lt;img alt&gt;</c>). Nullable.</summary>
    public string? AltText { get; set; }

    /// <summary>Display order within the product's gallery (ascending).</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Whether this is the product's primary/hero image. At most one per product (DB-enforced by
    /// the filtered unique index <c>UX_ProductImage_Primary</c>); its <see cref="BlobKey"/> is
    /// mirrored to <see cref="Product.PrimaryImageBlobKey"/> by the service.
    /// </summary>
    public bool IsPrimary { get; set; }

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
