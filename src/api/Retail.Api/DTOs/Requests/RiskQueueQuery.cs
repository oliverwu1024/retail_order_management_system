namespace Retail.Api.DTOs.Requests;

/// <summary>Query for the order-anomaly Risk Queue (Phase 5B). Bound from the query string (PascalCase).</summary>
public sealed record RiskQueueQuery
{
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
