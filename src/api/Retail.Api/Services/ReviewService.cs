using Retail.Api.Common.Models;
using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Exceptions;
using Retail.Api.Mappers;
using Retail.Api.Repositories;

namespace Retail.Api.Services;

/// <summary>Customer product reviews (Story 4.1). Owns the purchase-verified + one-per-product rules.</summary>
public sealed class ReviewService : IReviewService
{
    private const int MaxPageSize = 50;

    private readonly IReviewRepository _reviews;
    private readonly IOrderRepository _orders;   // purchase verification
    private readonly IProductRepository _products; // existence check
    private readonly ICustomerProfileService _profiles; // resolves the caller's profile

    public ReviewService(
        IReviewRepository reviews,
        IOrderRepository orders,
        IProductRepository products,
        ICustomerProfileService profiles)
    {
        _reviews = reviews;
        _orders = orders;
        _products = products;
        _profiles = profiles;
    }

    /// <inheritdoc />
    public async Task<ReviewDto> SubmitReviewAsync(string appUserId, Guid productId, SubmitReviewRequest request, CancellationToken ct)
    {
        CustomerProfileDto profile = await _profiles.GetMyProfileAsync(appUserId, ct);

        if (!await _products.ExistsByIdAsync(productId, ct))
        {
            throw new NotFoundException($"Product '{productId}' was not found.");
        }

        // REQUIREMENTS §6.1: only a customer who purchased (and completed) the order may review.
        if (!await _orders.HasPurchasedProductAsync(profile.Id, productId, ct))
        {
            throw new BusinessRuleException("You can only review a product you have purchased.");
        }

        // One review per customer per product (the UX_Review unique index is the backstop; a
        // concurrent duplicate insert surfaces as DbUpdateException 2601/2627 → 409).
        if (await _reviews.ExistsForCustomerAndProductAsync(productId, profile.Id, ct))
        {
            throw new ConflictException("You have already reviewed this product.");
        }

        var review = new Review
        {
            ProductId = productId,
            CustomerProfileId = profile.Id,
            Rating = (byte)request.Rating,
            Body = request.Body,
        };
        await _reviews.AddAsync(review, ct);
        await _reviews.SaveChangesAsync(ct);

        // C3 seam: a ReviewCreated signal is enqueued here for async Azure sentiment scoring (Chunk 3).
        // Until then SentimentScore/SentimentLabel stay null (the review is "unscored").

        return new ReviewDto(
            review.Id,
            profile.DisplayName,
            review.Rating,
            review.Body,
            review.SentimentScore,
            review.SentimentLabel?.ToString(),
            review.CreatedAt);
    }

    /// <inheritdoc />
    public async Task<ReviewListDto> ListReviewsAsync(Guid productId, int page, int pageSize, CancellationToken ct)
    {
        int safePage = page < 1 ? 1 : page;
        int safeSize = Math.Clamp(pageSize, 1, MaxPageSize);

        (IReadOnlyList<Review> items, int total) = await _reviews.ListByProductIdAsync(productId, safePage, safeSize, ct);
        ReviewSummaryDto summary = await _reviews.GetSummaryByProductIdAsync(productId, ct);

        var pageDto = new PagedResult<ReviewDto>(
            items.Select(r => r.ToDto()).ToList(),
            total,
            safePage,
            safeSize);

        return new ReviewListDto(pageDto, summary);
    }
}
