namespace Retail.Api.DTOs.Requests;

/// <summary>
/// Query-string parameters for the customer order listing (<c>GET /orders</c>). Bound as a single
/// <c>[FromQuery]</c> DTO so the OpenAPI surface reads <c>Page</c>/<c>PageSize</c> consistently with
/// the catalogue listing (<see cref="ProductListQuery"/>); the service clamps both to sane bounds.
/// </summary>
public sealed record OrderListQuery
{
    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Items per page.</summary>
    public int PageSize { get; init; } = 20;
}
