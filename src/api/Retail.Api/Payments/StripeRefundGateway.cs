using Microsoft.Extensions.Options;
using Stripe;

namespace Retail.Api.Payments;

/// <summary>Real <see cref="IStripeRefundGateway"/> — calls Stripe's Refund API.</summary>
public sealed class StripeRefundGateway : IStripeRefundGateway
{
    private readonly IStripeClient _stripe;

    public StripeRefundGateway(IOptions<StripeOptions> options)
    {
        _stripe = new StripeClient(options.Value.SecretKey);
    }

    public async Task RefundAsync(string paymentIntentId, CancellationToken ct)
    {
        var refundService = new RefundService(_stripe);
        await refundService.CreateAsync(
            new RefundCreateOptions { PaymentIntent = paymentIntentId },
            cancellationToken: ct);
    }
}
