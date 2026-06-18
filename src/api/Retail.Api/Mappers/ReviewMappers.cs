using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Mappers;

/// <summary>Entity → DTO projection for <see cref="Review"/> (Phase 4).</summary>
public static class ReviewMappers
{
    /// <summary>
    /// Maps a review to its storefront DTO. Expects <see cref="Review.CustomerProfile"/> to be
    /// loaded (the list query <c>Include</c>s it); falls back to a neutral label if the display
    /// name is missing so we never surface an empty author.
    /// </summary>
    public static ReviewDto ToDto(this Review review) => new(
        review.Id,
        ResolveName(review.CustomerProfile?.DisplayName),
        review.Rating,
        review.Body,
        review.SentimentScore,
        review.SentimentLabel?.ToString(),
        review.CreatedAt);

    private static string ResolveName(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? "A customer" : displayName;
}
