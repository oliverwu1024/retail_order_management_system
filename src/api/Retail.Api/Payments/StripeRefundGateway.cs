using Microsoft.Extensions.Options;
using Stripe;

namespace Retail.Api.Payments;

/// <summary>Real <see cref="IStripeRefundGateway"/> — calls Stripe's Refund API.</summary>
public sealed class StripeRefundGateway : IStripeRefundGateway
{
    // Built lazily: Stripe.net's StripeClient ctor REJECTS an empty key, so constructing eagerly
    // here would make every action on a controller that injects this gateway fail (even read-only
    // order endpoints that never refund) when no Stripe:SecretKey is configured. Deferring to first
    // use keeps "refunds are optional until configured" true.
    private readonly Lazy<IStripeClient> _stripe;

    public StripeRefundGateway(IOptions<StripeOptions> options)
    {
        _stripe = new Lazy<IStripeClient>(() => new StripeClient(options.Value.SecretKey));
    }

    public async Task RefundAsync(string paymentIntentId, CancellationToken ct)
    {
        var refundService = new RefundService(_stripe.Value);
        await refundService.CreateAsync(
            new RefundCreateOptions { PaymentIntent = paymentIntentId },
            // Deterministic idempotency key (derived from the PaymentIntent) so a redelivered or
            // racing refund request collapses to one actual refund at Stripe — defence-in-depth
            // behind the Paid → Refunding claim in OrderCancellationService.
            new RequestOptions { IdempotencyKey = $"refund:{paymentIntentId}" },
            ct);
    }
}
