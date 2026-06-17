namespace Retail.Api.DTOs.Requests;

/// <summary>Paged + role-filtered query for the admin user list (Phase 3 §10).</summary>
public sealed record AdminUserListQuery
{
    /// <summary>Optional role filter (e.g. <c>"Staff"</c>). Null/empty = all accounts.</summary>
    public string? Role { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
