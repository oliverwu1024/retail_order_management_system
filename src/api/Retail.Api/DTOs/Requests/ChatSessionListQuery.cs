namespace Retail.Api.DTOs.Requests;

/// <summary>Query for the admin chat-session list (Phase 5A). Bound from the query string (PascalCase).</summary>
public sealed record ChatSessionListQuery
{
    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
