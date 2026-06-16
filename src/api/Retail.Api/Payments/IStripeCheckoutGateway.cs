namespace Retail.Api.Payments;

/// <summary>
/// Thin abstraction over Stripe's hosted-Checkout Session creation. The real implementation
/// makes a network call to Stripe; integration tests substitute a fake so they never touch the
/// network. Webhook signature verification is handled separately (offline, via EventUtility),
/// so it isn't on this interface.
/// </summary>
public interface IStripeCheckoutGateway
{
    /// <summary>Creates a Stripe hosted Checkout Session and returns its id + redirect URL.</summary>
    Task<StripeCheckoutSession> CreateCheckoutSessionAsync(CheckoutSessionRequest request, CancellationToken ct);
}

/// <summary>What we ask Stripe to bill for. Money is integer cents (Stripe's minor units too).</summary>
public sealed record CheckoutSessionRequest(
    IReadOnlyList<CheckoutLineItem> LineItems,
    string Currency,
    string SuccessUrl,
    string CancelUrl,
    string? CustomerEmail,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>One line on the hosted checkout page.</summary>
public sealed record CheckoutLineItem(string Name, int UnitAmountCents, int Quantity);

/// <summary>The created session — its id (persisted on the Payment) and the URL to redirect the shopper to.</summary>
public sealed record StripeCheckoutSession(string SessionId, string Url);
