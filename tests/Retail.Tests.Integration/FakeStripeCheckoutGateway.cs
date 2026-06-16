using Retail.Api.Payments;

namespace Retail.Tests.Integration;

/// <summary>
/// Fake <see cref="IStripeCheckoutGateway"/> for hermetic checkout tests — returns a canned
/// session id + URL without calling Stripe over the network.
/// </summary>
public sealed class FakeStripeCheckoutGateway : IStripeCheckoutGateway
{
    public Task<StripeCheckoutSession> CreateCheckoutSessionAsync(CheckoutSessionRequest request, CancellationToken ct)
    {
        string sessionId = $"cs_test_{Guid.NewGuid():N}";
        return Task.FromResult(new StripeCheckoutSession(sessionId, $"https://stripe.test/checkout/{sessionId}"));
    }
}
