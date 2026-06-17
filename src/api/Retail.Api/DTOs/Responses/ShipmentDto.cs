namespace Retail.Api.DTOs.Responses;

/// <summary>The fulfilment shipment on the admin order detail (Phase 3 §8). Null on an unshipped order.</summary>
public sealed record ShipmentDto(
    string? Carrier,
    string? TrackingNumber,
    string Status,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? DeliveredAt);
