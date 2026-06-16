using Retail.Api.Payments;

namespace Retail.Tests.Integration;

/// <summary>Fake <see cref="IStripeRefundGateway"/> for hermetic cancel tests — a no-op (no Stripe call).</summary>
public sealed class FakeStripeRefundGateway : IStripeRefundGateway
{
    public Task RefundAsync(string paymentIntentId, CancellationToken ct) => Task.CompletedTask;
}
