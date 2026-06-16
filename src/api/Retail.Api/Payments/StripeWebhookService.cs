using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Retail.Api.Domain.Entities;
using Retail.Api.Services;
using Stripe;
using Stripe.Checkout;

namespace Retail.Api.Payments;

/// <summary>
/// Stripe webhook handler (Story 2.2). Verifies the signature, skips redeliveries, and turns a
/// completed checkout into an order.
/// </summary>
/// <remarks>
/// Idempotency is layered: the per-session unique index on <c>Payment.StripeSessionId</c> (inside
/// <see cref="IOrderCreationService"/>) is the hard guarantee; the <see cref="IProcessedStripeEventStore"/>
/// is a fast-path skip + audit ledger. We record the event AFTER successful handling, so a failure
/// leaves it un-recorded and Stripe's retry re-processes it safely.
/// </remarks>
public sealed class StripeWebhookService : IStripeWebhookService
{
    private readonly StripeOptions _options;
    private readonly IProcessedStripeEventStore _events;
    private readonly IOrderCreationService _orderCreation;
    private readonly IOrderRefundService _orderRefund;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StripeWebhookService> _logger;

    public StripeWebhookService(
        IOptions<StripeOptions> options,
        IProcessedStripeEventStore events,
        IOrderCreationService orderCreation,
        IOrderRefundService orderRefund,
        TimeProvider timeProvider,
        ILogger<StripeWebhookService> logger)
    {
        _options = options.Value;
        _events = events;
        _orderCreation = orderCreation;
        _orderRefund = orderRefund;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task HandleAsync(string payload, string signatureHeader, CancellationToken ct)
    {
        // Verify the signature against our webhook secret (offline HMAC). Throws on mismatch.
        // throwOnApiVersionMismatch:false — real events carry the Stripe ACCOUNT's API version,
        // which need not equal the SDK's; a version difference must not reject a valid event.
        Event stripeEvent = EventUtility.ConstructEvent(
            payload, signatureHeader, _options.WebhookSigningSecret, throwOnApiVersionMismatch: false);

        if (await _events.IsProcessedAsync(stripeEvent.Id, ct))
        {
            _logger.LogInformation("Stripe event {EventId} already processed; skipping.", stripeEvent.Id);
            return;
        }

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                await HandleCheckoutCompletedAsync(stripeEvent, ct);
                break;
            case "charge.refunded":
                await HandleChargeRefundedAsync(stripeEvent, ct);
                break;
            default:
                _logger.LogInformation("Ignoring unhandled Stripe event {EventType} ({EventId}).", stripeEvent.Type, stripeEvent.Id);
                break;
        }

        await _events.RecordAsync(stripeEvent.Id, stripeEvent.Type, _timeProvider.GetUtcNow(), ct);
    }

    private async Task HandleCheckoutCompletedAsync(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not Session session)
        {
            _logger.LogWarning("checkout.session.completed {EventId} had no Session payload.", stripeEvent.Id);
            return;
        }

        if (!session.Metadata.TryGetValue("cartId", out string? cartIdRaw) || !Guid.TryParse(cartIdRaw, out Guid cartId))
        {
            _logger.LogWarning("checkout.session.completed {EventId} has no valid cartId metadata; ignoring.", stripeEvent.Id);
            return;
        }

        Guid? customerProfileId = null;
        if (session.Metadata.TryGetValue("customerProfileId", out string? profileRaw) && Guid.TryParse(profileRaw, out Guid pid))
        {
            customerProfileId = pid;
        }

        // MVP: the billing address Stripe collected doubles as the shipping address.
        OrderAddressSnapshot address = ToSnapshot(session.CustomerDetails);

        var completion = new CheckoutCompletion(
            StripeSessionId: session.Id,
            PaymentIntentId: session.PaymentIntentId,
            CartId: cartId,
            CustomerProfileId: customerProfileId,
            CustomerEmail: session.CustomerDetails?.Email ?? session.CustomerEmail,
            ShippingAddress: address,
            BillingAddress: address);

        Order order = await _orderCreation.CreateOrderFromCheckoutAsync(completion, ct);
        _logger.LogInformation(
            "checkout.session.completed {EventId} -> order {OrderId} (#{OrderNumber}).",
            stripeEvent.Id, order.Id, order.OrderNumber);
    }

    private async Task HandleChargeRefundedAsync(Event stripeEvent, CancellationToken ct)
    {
        if (stripeEvent.Data.Object is not Charge charge)
        {
            _logger.LogWarning("charge.refunded {EventId} had no Charge payload.", stripeEvent.Id);
            return;
        }

        if (string.IsNullOrEmpty(charge.PaymentIntentId))
        {
            _logger.LogWarning("charge.refunded {EventId} has no PaymentIntent; ignoring.", stripeEvent.Id);
            return;
        }

        await _orderRefund.RefundByPaymentIntentAsync(charge.PaymentIntentId, ct);
        _logger.LogInformation(
            "charge.refunded {EventId} -> refunded PaymentIntent {PaymentIntentId}.",
            stripeEvent.Id, charge.PaymentIntentId);
    }

    private static OrderAddressSnapshot ToSnapshot(SessionCustomerDetails? details)
    {
        Stripe.Address? address = details?.Address;
        return new OrderAddressSnapshot
        {
            RecipientName = details?.Name,
            Line1 = address?.Line1 ?? string.Empty,
            Line2 = address?.Line2,
            City = address?.City ?? string.Empty,
            Region = address?.State,
            PostalCode = address?.PostalCode ?? string.Empty,
            Country = address?.Country ?? string.Empty,
        };
    }
}
