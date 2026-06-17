using Microsoft.Extensions.Options;
using Retail.Api.Payments;

namespace Retail.Tests.Unit.Payments;

/// <summary>
/// The real Stripe gateways must CONSTRUCT without a configured secret key. Stripe.net's
/// <c>StripeClient</c> ctor rejects an empty key, so an eager build would make every action on a
/// controller that injects these gateways fail — including read-only order endpoints that never
/// touch Stripe — in any environment with no key. The gateways build the client lazily instead;
/// the failure should only surface if a real Stripe call is actually made.
/// </summary>
public class StripeGatewayConstructionTests
{
    private static IOptions<StripeOptions> NoKey() =>
        Options.Create(new StripeOptions { SecretKey = string.Empty, WebhookSigningSecret = string.Empty });

    [Fact]
    public void RefundGateway_ConstructsWithoutSecretKey()
    {
        Exception? ex = Record.Exception(() => new StripeRefundGateway(NoKey()));
        Assert.Null(ex);
    }

    [Fact]
    public void CheckoutGateway_ConstructsWithoutSecretKey()
    {
        Exception? ex = Record.Exception(() => new StripeCheckoutGateway(NoKey()));
        Assert.Null(ex);
    }
}
