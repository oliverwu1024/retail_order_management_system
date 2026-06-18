namespace Retail.Api.DTOs.Requests;

/// <summary>
/// Query-string parameters for the public product-review listing
/// (<c>GET /api/v1/products/{id}/reviews</c>). Bound as one <c>[FromQuery]</c> DTO,
/// consistent with <c>ProductListQuery</c> / <c>OrderListQuery</c>; the service clamps both.
/// </summary>
public sealed record ReviewListQuery
{
    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Items per page.</summary>
    public int PageSize { get; init; } = 10;
}
