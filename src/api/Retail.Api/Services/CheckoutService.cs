using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Exceptions;
using Retail.Api.Payments;

namespace Retail.Api.Services;

/// <summary>
/// Checkout orchestration (Story 2.2). Start = reserve the cart + create a Stripe hosted
/// Checkout Session; the matching "complete" (order creation) is driven by the webhook in 3c.
/// </summary>
public sealed class CheckoutService : ICheckoutService
{
    private const double GstRate = 0.10; // flat 10% GST (MVP)

    private readonly ICartService _carts;
    private readonly IInventoryReservationService _reservations;
    private readonly ICustomerProfileService _profiles;
    private readonly IStripeCheckoutGateway _gateway;

    public CheckoutService(
        ICartService carts,
        IInventoryReservationService reservations,
        ICustomerProfileService profiles,
        IStripeCheckoutGateway gateway)
    {
        _carts = carts;
        _reservations = reservations;
        _profiles = profiles;
        _gateway = gateway;
    }

    /// <inheritdoc />
    public async Task<CheckoutSessionResponse> StartCheckoutAsync(CartCaller caller, StartCheckoutRequest request, CancellationToken ct)
    {
        // Resolve (and merge-on-login) the caller's cart.
        CartResult cartResult = await _carts.GetCartAsync(caller, ct);
        CartDto cart = cartResult.Cart;
        if (cart.Items.Count == 0)
        {
            throw new ConflictException("Your cart is empty.");
        }

        Guid cartId = cart.Id;

        // Money math in long, then bounded into int — mirrors OrderCreationService. We compute
        // (and guard) the total BEFORE reserving stock or creating the Stripe session: an
        // oversized cart must be rejected here, not charged at Stripe and then rejected at order
        // creation (which would take the customer's money without producing an order). Computed
        // from the line items, not cart.SubtotalCents, which is itself an int that could wrap.
        long subtotalLong = cart.Items.Sum(item => (long)item.UnitPriceCents * item.Quantity);
        long taxLong = (long)Math.Round(subtotalLong * GstRate, MidpointRounding.AwayFromZero);
        if (subtotalLong + taxLong > int.MaxValue)
        {
            throw new ConflictException("Order total exceeds the maximum supported amount.");
        }
        int taxCents = (int)taxLong;

        // Hold the stock for the duration of checkout (throws 409 if it can't be held).
        await _reservations.ReserveCartAsync(cartId, ct);

        var lineItems = cart.Items
            .Select(item => new CheckoutLineItem(item.ProductName, item.UnitPriceCents, item.Quantity))
            .ToList();
        if (taxCents > 0)
        {
            // Bill tax as its own line so Stripe's charged total equals our order total exactly.
            lineItems.Add(new CheckoutLineItem("GST (10%)", taxCents, 1));
        }

        // The webhook reads this metadata to know which cart to finalise and whether it's a
        // member order (CustomerProfileId set) or a guest order.
        var metadata = new Dictionary<string, string> { ["cartId"] = cartId.ToString() };
        string? customerEmail = null;
        if (caller.AppUserId is { Length: > 0 } appUserId)
        {
            CustomerProfileDto profile = await _profiles.GetMyProfileAsync(appUserId, ct);
            metadata["customerProfileId"] = profile.Id.ToString();
            customerEmail = profile.Email; // prefill so the member doesn't retype it
        }

        string baseUrl = request.ReturnBaseUrl.TrimEnd('/');
        var gatewayRequest = new CheckoutSessionRequest(
            LineItems: lineItems,
            Currency: "AUD",
            // Stripe substitutes the real session id into {CHECKOUT_SESSION_ID}; the success
            // page uses it to look up the (webhook-created) order.
            SuccessUrl: $"{baseUrl}/checkout/success?session_id={{CHECKOUT_SESSION_ID}}",
            CancelUrl: $"{baseUrl}/cart",
            CustomerEmail: customerEmail,
            Metadata: metadata);

        StripeCheckoutSession session = await _gateway.CreateCheckoutSessionAsync(gatewayRequest, ct);
        return new CheckoutSessionResponse(session.Url);
    }
}
