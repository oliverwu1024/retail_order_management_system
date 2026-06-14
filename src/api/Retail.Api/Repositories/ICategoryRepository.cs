using Retail.Api.Domain.Entities;

namespace Retail.Api.Repositories;

/// <summary>Data access for <see cref="Category"/>.</summary>
public interface ICategoryRepository
{
    /// <summary>All non-deleted categories, ordered by name.</summary>
    Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct);

    /// <summary>A non-deleted category by id (tracked), or null. Used for the parent-depth walk.</summary>
    Task<Category?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Whether a non-deleted category uses this slug.</summary>
    Task<bool> SlugExistsAsync(string slug, CancellationToken ct);

    /// <summary>Whether a non-deleted category with this id exists (validates a product's CategoryId).</summary>
    Task<bool> ExistsAsync(Guid id, CancellationToken ct);

    /// <summary>Stages a new category for insert.</summary>
    Task AddAsync(Category category, CancellationToken ct);

    /// <summary>Persists tracked changes.</summary>
    Task SaveChangesAsync(CancellationToken ct);
}
