namespace Retail.Api.DTOs.Responses;

/// <summary>An order as it appears in the customer's order list (Story 2.4).</summary>
public sealed record OrderSummaryDto(
    Guid Id,
    int OrderNumber,
    string Status,
    DateTimeOffset PlacedAt,
    int TotalCents,
    int ItemCount);
