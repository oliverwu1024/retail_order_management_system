using Microsoft.EntityFrameworkCore;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Repositories;

/// <summary>EF Core implementation of <see cref="ICategoryRepository"/>.</summary>
public sealed class CategoryRepository : ICategoryRepository
{
    private readonly RetailDbContext _db;

    public CategoryRepository(RetailDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct) =>
        await _db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<Category?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await _db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct);

    /// <inheritdoc />
    public async Task<bool> SlugExistsAsync(string slug, CancellationToken ct) =>
        await _db.Categories.AnyAsync(c => c.Slug == slug, ct);

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(Guid id, CancellationToken ct) =>
        await _db.Categories.AnyAsync(c => c.Id == id, ct);

    /// <inheritdoc />
    public async Task AddAsync(Category category, CancellationToken ct) =>
        await _db.Categories.AddAsync(category, ct);

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken ct) =>
        await _db.SaveChangesAsync(ct);
}
