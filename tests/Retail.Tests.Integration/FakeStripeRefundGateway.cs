using System.Collections.Concurrent;
using Retail.Api.Payments;

namespace Retail.Tests.Integration;

/// <summary>
/// Fake <see cref="IStripeRefundGateway"/> for hermetic cancel tests — no Stripe call. Records how
/// many times a refund was issued per PaymentIntent so the concurrency test can assert that two
/// racing cancels still hit Stripe exactly once (the Paid → Refunding claim guarantees it).
/// </summary>
public sealed class FakeStripeRefundGateway : IStripeRefundGateway
{
    private static readonly ConcurrentDictionary<string, int> RefundCalls = new();

    public Task RefundAsync(string paymentIntentId, CancellationToken ct)
    {
        RefundCalls.AddOrUpdate(paymentIntentId, 1, (_, count) => count + 1);
        return Task.CompletedTask;
    }

    /// <summary>How many refunds the gateway was asked to issue for a PaymentIntent (test-only). Keyed
    /// by the unique PaymentIntent id, so counts never bleed between tests.</summary>
    public static int RefundCountFor(string paymentIntentId) =>
        RefundCalls.TryGetValue(paymentIntentId, out int count) ? count : 0;
}
