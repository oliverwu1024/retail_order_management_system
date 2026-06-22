namespace Retail.Api.DTOs.Responses;

/// <summary>The latest demand forecast for a variant (Phase 5B §8). Carries the variant label the FE needs.</summary>
public sealed record ForecastDto(
    Guid ProductVariantId,
    string Sku,
    string ProductName,
    decimal ForecastedQty,
    decimal LowerBound,
    decimal UpperBound,
    decimal Confidence,
    DateTimeOffset GeneratedAt);
