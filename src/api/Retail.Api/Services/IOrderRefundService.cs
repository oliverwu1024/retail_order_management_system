namespace Retail.Api.Services;

/// <summary>
/// Reverses a paid order when Stripe reports a refund (Story 2.2) — driven by the
/// <c>charge.refunded</c> webhook.
/// </summary>
public interface IOrderRefundService
{
    /// <summary>
    /// Marks the order behind a Stripe PaymentIntent as Refunded, restocks its lines, and records
    /// a negative payment — all in one transaction. Idempotent (an already-refunded order is a
    /// no-op) and a no-op if no order matches the intent.
    /// </summary>
    Task RefundByPaymentIntentAsync(string paymentIntentId, CancellationToken ct);
}
