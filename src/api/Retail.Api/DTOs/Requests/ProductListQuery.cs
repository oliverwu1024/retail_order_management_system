namespace Retail.Api.DTOs.Requests;

/// <summary>
/// Query-string parameters for the public catalogue listing (<c>GET /products</c>).
/// Bound from the query string; the service clamps page/size to sane bounds.
/// </summary>
public sealed record ProductListQuery
{
    /// <summary>1-based page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Items per page.</summary>
    public int PageSize { get; init; } = 20;

    /// <summary>Optional category filter.</summary>
    public Guid? CategoryId { get; init; }

    /// <summary>Optional case-insensitive name/description search.</summary>
    public string? Search { get; init; }
}
