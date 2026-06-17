namespace Retail.Api.DTOs.Requests;

/// <summary>Search + paging for the audit-log viewer (Phase 3 §7). All filters optional.</summary>
public sealed record AuditLogListQuery
{
    /// <summary>Exact actor (Identity user id, or <c>"system"</c>).</summary>
    public string? Actor { get; init; }

    /// <summary>Exact entity type (e.g. <c>"Order"</c>).</summary>
    public string? EntityType { get; init; }

    /// <summary>Exact entity id — usually combined with <see cref="EntityType"/> to trace one record.</summary>
    public string? EntityId { get; init; }

    /// <summary>Inclusive lower bound on <c>OccurredAt</c> (UTC).</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Exclusive upper bound on <c>OccurredAt</c> (UTC).</summary>
    public DateTimeOffset? To { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
