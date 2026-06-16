namespace Retail.Api.Payments;

/// <summary>
/// The Stripe webhook idempotency ledger (Story 2.2) — records which events we've handled so a
/// redelivery is skipped. The hard idempotency guarantee for order creation is the unique index
/// on <c>Payment.StripeSessionId</c>; this store is the fast-path skip + audit trail.
/// </summary>
public interface IProcessedStripeEventStore
{
    /// <summary>Whether this Stripe event id has already been recorded as handled.</summary>
    Task<bool> IsProcessedAsync(string stripeEventId, CancellationToken ct);

    /// <summary>
    /// Records an event as handled. Best-effort + idempotent: a concurrent delivery that recorded
    /// it first (unique-index violation) is swallowed, not surfaced.
    /// </summary>
    Task RecordAsync(string stripeEventId, string eventType, DateTimeOffset receivedAt, CancellationToken ct);
}
