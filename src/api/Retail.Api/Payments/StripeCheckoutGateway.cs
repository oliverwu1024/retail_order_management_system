using Microsoft.Extensions.Options;
using Stripe;
using Stripe.Checkout;

namespace Retail.Api.Payments;

/// <summary>
/// Real <see cref="IStripeCheckoutGateway"/> — talks to Stripe over the network via Stripe.net.
/// </summary>
/// <remarks>
/// Builds a <see cref="StripeClient"/> from the configured secret key (rather than the global
/// static <c>StripeConfiguration.ApiKey</c>) so the credential is instance-scoped and easy to
/// reason about. If the key is blank the client still constructs — the failure surfaces only
/// when a real call is made, which is the intended "checkout is optional until configured"
/// behaviour.
/// </remarks>
public sealed class StripeCheckoutGateway : IStripeCheckoutGateway
{
    private readonly IStripeClient _stripe;

    public StripeCheckoutGateway(IOptions<StripeOptions> options)
    {
        _stripe = new StripeClient(options.Value.SecretKey);
    }

    public async Task<StripeCheckoutSession> CreateCheckoutSessionAsync(CheckoutSessionRequest request, CancellationToken ct)
    {
        var createOptions = new SessionCreateOptions
        {
            Mode = "payment",
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            CustomerEmail = request.CustomerEmail,
            // Stripe collects the billing address on its hosted page; the webhook reads it back
            // from session.customer_details.address (used for both billing + shipping in the MVP).
            BillingAddressCollection = "required",
            // Round-trips our cart id + buyer identity through Stripe so the webhook knows
            // which cart/order to finalise when checkout.session.completed comes back.
            Metadata = new Dictionary<string, string>(request.Metadata),
            LineItems = request.LineItems
                .Select(line => new SessionLineItemOptions
                {
                    Quantity = line.Quantity,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = request.Currency.ToLowerInvariant(),
                        UnitAmount = line.UnitAmountCents, // already minor units (cents)
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = line.Name,
                        },
                    },
                })
                .ToList(),
        };

        var sessionService = new SessionService(_stripe);
        Session session = await sessionService.CreateAsync(createOptions, cancellationToken: ct);
        return new StripeCheckoutSession(session.Id, session.Url);
    }
}
