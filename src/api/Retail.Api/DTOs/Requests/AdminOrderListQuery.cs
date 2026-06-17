namespace Retail.Api.DTOs.Requests;

/// <summary>Filters + paging for the admin order workbench (Phase 3 §8).</summary>
public sealed record AdminOrderListQuery
{
    /// <summary>Filter by order status name (e.g. <c>"Paid"</c>). Null/blank = all statuses.</summary>
    public string? Status { get; init; }

    /// <summary>Inclusive lower bound on <c>PlacedAt</c> (UTC). Null = no lower bound.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Exclusive upper bound on <c>PlacedAt</c> (UTC). Null = no upper bound.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Substring match on the buyer's email (guest email OR member email). Null/blank = all.</summary>
    public string? CustomerEmail { get; init; }

    /// <summary>Placeholder for the Phase-5 anomaly flag — accepted but a no-op in Phase 3.</summary>
    public bool? HasAnomaly { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
