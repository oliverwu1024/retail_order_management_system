using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// A product category. Self-referencing tree (max depth 3, enforced in the
/// service) — see DATABASE_DESIGN §3.4. Soft-deletable.
/// </summary>
public class Category : IAuditableEntity
{
    /// <summary>Surrogate PK (DB-generated sequential GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>URL-safe identifier, unique among non-deleted categories.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Parent category, or null for a top-level category.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Navigation to the parent.</summary>
    public Category? Parent { get; set; }

    /// <summary>Navigation to child categories.</summary>
    public ICollection<Category> Children { get; set; } = new List<Category>();

    /// <summary>Products directly in this category.</summary>
    public ICollection<Product> Products { get; set; } = new List<Product>();

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
