namespace Retail.Api.DTOs.Requests;

/// <summary>Query for the forecast + reorder-hint lists (Phase 5B). Bound from the query string (PascalCase).</summary>
public sealed record ForecastListQuery
{
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
