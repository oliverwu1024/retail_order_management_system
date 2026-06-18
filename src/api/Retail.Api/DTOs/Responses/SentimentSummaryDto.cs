namespace Retail.Api.DTOs.Responses;

/// <summary>
/// Review-sentiment dashboard aggregate (Phase 4, Story 4.3): the overall average, the count scored,
/// the label distribution (for the chart), and per-product averages (worst-first).
/// </summary>
public sealed record SentimentSummaryDto(
    double? AverageScore,
    int ScoredReviews,
    IReadOnlyList<LabelCountDto> LabelDistribution,
    IReadOnlyList<ProductSentimentDto> Products);

/// <summary>Count of scored reviews carrying a given sentiment label.</summary>
public sealed record LabelCountDto(string Label, int Count);

/// <summary>A product's average sentiment over its scored reviews.</summary>
public sealed record ProductSentimentDto(Guid ProductId, string ProductName, double AverageScore, int ReviewCount);
