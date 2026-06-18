using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Repositories;

/// <summary>Persistence for product <see cref="Review"/>s (Phase 4). Pure data access — the
/// purchase-verified / one-per-customer rules live in <c>ReviewService</c>.</summary>
public interface IReviewRepository
{
    /// <summary>Stages a new review for insert.</summary>
    Task AddAsync(Review review, CancellationToken ct);

    /// <summary>Whether this customer already has a (non-deleted) review for the product — the dup guard.</summary>
    Task<bool> ExistsForCustomerAndProductAsync(Guid productId, Guid customerProfileId, CancellationToken ct);

    /// <summary>A read-only page of a product's reviews (newest first), each with its author profile.</summary>
    Task<(IReadOnlyList<Review> Items, int Total)> ListByProductIdAsync(Guid productId, int page, int pageSize, CancellationToken ct);

    /// <summary>Whole-product aggregate (average + count + 1..5 distribution) over all non-deleted reviews.</summary>
    Task<ReviewSummaryDto> GetSummaryByProductIdAsync(Guid productId, CancellationToken ct);

    /// <summary>Persists tracked changes.</summary>
    Task SaveChangesAsync(CancellationToken ct);
}
