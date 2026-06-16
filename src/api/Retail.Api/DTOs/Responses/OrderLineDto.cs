namespace Retail.Api.DTOs.Responses;

/// <summary>A line on an order, from its snapshots (Story 2.4).</summary>
public sealed record OrderLineDto(
    string ProductName,
    string Sku,
    int Quantity,
    int UnitPriceCents,
    int LineTotalCents);
