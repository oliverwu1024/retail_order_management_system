namespace Retail.Api.DTOs.Responses;

/// <summary>An order row in the admin workbench list (Phase 3 §8) — adds the buyer's email and the
/// fulfilment (shipment) status the customer-facing summary omits.</summary>
public sealed record AdminOrderSummaryDto(
    Guid Id,
    int OrderNumber,
    string Status,
    DateTimeOffset PlacedAt,
    int TotalCents,
    int ItemCount,
    string CustomerEmail,
    string? ShipmentStatus);
