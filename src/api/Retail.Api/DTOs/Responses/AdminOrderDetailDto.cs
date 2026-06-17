namespace Retail.Api.DTOs.Responses;

/// <summary>The full order for the admin workbench detail (Phase 3 §8). Unlike the customer
/// <see cref="OrderDetailDto"/>, it surfaces the buyer's email, the payment ledger, and the shipment.</summary>
public sealed record AdminOrderDetailDto(
    Guid Id,
    int OrderNumber,
    string Status,
    DateTimeOffset PlacedAt,
    string CustomerEmail,
    int SubtotalCents,
    int TaxCents,
    int ShippingCents,
    int TotalCents,
    OrderAddressDto ShippingAddress,
    OrderAddressDto BillingAddress,
    IReadOnlyList<OrderLineDto> Lines,
    IReadOnlyList<PaymentDto> Payments,
    ShipmentDto? Shipment);
