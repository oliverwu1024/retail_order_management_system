namespace Retail.Api.DTOs.Responses;

/// <summary>The full order for the detail page (Story 2.4) — totals, addresses, and line items.</summary>
public sealed record OrderDetailDto(
    Guid Id,
    int OrderNumber,
    string Status,
    DateTimeOffset PlacedAt,
    int SubtotalCents,
    int TaxCents,
    int ShippingCents,
    int TotalCents,
    OrderAddressDto ShippingAddress,
    OrderAddressDto BillingAddress,
    IReadOnlyList<OrderLineDto> Lines);
