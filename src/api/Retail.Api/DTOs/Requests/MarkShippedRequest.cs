namespace Retail.Api.DTOs.Requests;

/// <summary>Body for "Mark as Shipped" — the carrier + tracking number (Phase 3 §8).</summary>
public sealed record MarkShippedRequest(string Carrier, string TrackingNumber);
