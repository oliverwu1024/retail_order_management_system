namespace Retail.Api.DTOs.Requests;

/// <summary>Date range for the sales-by-day report (Phase 3 §9). Both optional — defaults to the last 30 days.</summary>
public sealed record SalesByDayQuery
{
    /// <summary>Inclusive lower bound on <c>PlacedAt</c> (UTC).</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Exclusive upper bound on <c>PlacedAt</c> (UTC).</summary>
    public DateTimeOffset? To { get; init; }
}
