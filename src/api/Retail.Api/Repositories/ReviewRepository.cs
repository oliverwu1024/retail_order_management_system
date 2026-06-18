using Microsoft.EntityFrameworkCore;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IReviewRepository"/>. The soft-delete global query
/// filter (RetailDbContext) keeps deleted reviews out of every read + the dup/aggregate counts.
/// </summary>
public sealed class ReviewRepository : IReviewRepository
{
    private readonly RetailDbContext _db;

    public ReviewRepository(RetailDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task AddAsync(Review review, CancellationToken ct) =>
        await _db.Reviews.AddAsync(review, ct);

    /// <inheritdoc />
    public async Task<bool> ExistsForCustomerAndProductAsync(Guid productId, Guid customerProfileId, CancellationToken ct) =>
        await _db.Reviews.AnyAsync(r => r.ProductId == productId && r.CustomerProfileId == customerProfileId, ct);

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Review> Items, int Total)> ListByProductIdAsync(
        Guid productId, int page, int pageSize, CancellationToken ct)
    {
        IQueryable<Review> query = _db.Reviews.AsNoTracking().Where(r => r.ProductId == productId);

        int total = await query.CountAsync(ct);

        List<Review> items = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(r => r.CustomerProfile)
            .ToListAsync(ct);

        return (items, total);
    }

    /// <inheritdoc />
    public async Task<ReviewSummaryDto> GetSummaryByProductIdAsync(Guid productId, CancellationToken ct)
    {
        // One GROUP BY Rating round-trip; assemble the 1..5 distribution + average in memory.
        var buckets = await _db.Reviews.AsNoTracking()
            .Where(r => r.ProductId == productId)
            .GroupBy(r => r.Rating)
            .Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var distribution = new int[5];
        int total = 0;
        int weighted = 0;
        foreach (var bucket in buckets)
        {
            distribution[bucket.Rating - 1] = bucket.Count; // Rating 1..5 → index 0..4
            total += bucket.Count;
            weighted += bucket.Rating * bucket.Count;
        }

        double average = total == 0 ? 0 : Math.Round(weighted / (double)total, 2);
        return new ReviewSummaryDto(average, total, distribution);
    }

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken ct) =>
        await _db.SaveChangesAsync(ct);
}
