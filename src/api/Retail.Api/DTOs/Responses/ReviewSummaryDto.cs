namespace Retail.Api.DTOs.Responses;

/// <summary>
/// Aggregate of ALL non-deleted reviews for a product (REQUIREMENTS §6.2: 平均评分 + 评分分布柱状图).
/// Computed across the whole product, not just the current page, so the storefront's average
/// and rating-distribution bar chart are correct regardless of paging.
/// </summary>
/// <param name="Average">Mean rating (0 when there are no reviews), rounded to 2 dp.</param>
/// <param name="Count">Total non-deleted reviews.</param>
/// <param name="Distribution">
/// Star counts, length 5: index 0 = number of 1-star reviews … index 4 = number of 5-star reviews.
/// </param>
public sealed record ReviewSummaryDto(
    double Average,
    int Count,
    IReadOnlyList<int> Distribution);
