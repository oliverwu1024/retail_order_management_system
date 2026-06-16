using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// A catalogue product (DATABASE_DESIGN §3.5). Has one or more
/// <see cref="ProductVariant"/>s that carry the actual price + stock. Soft-deletable;
/// only published, non-deleted products appear on the storefront.
/// </summary>
public class Product : IAuditableEntity
{
    /// <summary>Surrogate PK (DB-generated sequential GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>Stock-keeping unit; unique among non-deleted products.</summary>
    public string Sku { get; set; } = string.Empty;

    /// <summary>URL-safe identifier; unique among non-deleted products. Used by <c>GET /products/{slug}</c>.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Long description (Markdown/rich text). Nullable.</summary>
    public string? Description { get; set; }

    /// <summary>SEO <c>&lt;title&gt;</c> override. Nullable.</summary>
    public string? SeoTitle { get; set; }

    /// <summary>SEO meta description. Nullable.</summary>
    public string? SeoDescription { get; set; }

    /// <summary>Brand name. Nullable.</summary>
    public string? BrandName { get; set; }

    /// <summary>FK to the owning <see cref="Category"/>.</summary>
    public Guid CategoryId { get; set; }

    /// <summary>Navigation to the category.</summary>
    public Category? Category { get; set; }

    /// <summary>Whether the product is visible on the storefront.</summary>
    public bool IsPublished { get; set; }

    /// <summary>
    /// Denormalized cache of the primary <see cref="ProductImage"/>'s blob key, kept in sync by
    /// the catalog service. Lets list/cart cards render the hero image with a single-column read
    /// instead of joining the gallery (<c>ProductSummaryDto</c> / <c>CartItemDto</c>).
    /// </summary>
    public string? PrimaryImageBlobKey { get; set; }

    /// <summary>The product's variants (price + options + stock live here).</summary>
    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();

    /// <summary>The product's image gallery (general + variant-specific). See <see cref="ProductImage"/>.</summary>
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();

    /// <summary>Soft-delete flag — hidden by the global query filter when true.</summary>
    public bool IsDeleted { get; set; }

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
