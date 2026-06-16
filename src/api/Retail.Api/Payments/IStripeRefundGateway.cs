namespace Retail.Api.Payments;

/// <summary>
/// Issues a refund against a Stripe PaymentIntent (Story 2.4 — customer cancel). Real impl calls
/// Stripe; tests fake it. Stripe also emits a <c>charge.refunded</c> webhook afterwards, which the
/// order-side handler treats idempotently.
/// </summary>
public interface IStripeRefundGateway
{
    /// <summary>Refunds the full charge on a PaymentIntent.</summary>
    Task RefundAsync(string paymentIntentId, CancellationToken ct);
}
