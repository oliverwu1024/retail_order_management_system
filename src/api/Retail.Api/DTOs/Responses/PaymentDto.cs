namespace Retail.Api.DTOs.Responses;

/// <summary>A payment-ledger row for the admin order detail (Phase 3 §8). Amount is signed cents
/// (positive charge, negative refund).</summary>
public sealed record PaymentDto(
    string Provider,
    int AmountCents,
    string Currency,
    string Status,
    string? StripePaymentIntentId,
    DateTimeOffset CreatedAt);
