namespace Retail.Api.Payments;

/// <summary>
/// Handles inbound Stripe webhook events (Story 2.2): verifies the signature, dedups, and
/// dispatches to the right side-effect (order creation on <c>checkout.session.completed</c>).
/// </summary>
public interface IStripeWebhookService
{
    /// <summary>
    /// Verifies the payload signature, then (if not already handled) dispatches the event. Throws
    /// <c>Stripe.StripeException</c> on a bad/missing signature — the controller maps that to 400.
    /// </summary>
    Task HandleAsync(string payload, string signatureHeader, CancellationToken ct);
}
