namespace Retail.Api.Payments;

/// <summary>
/// Stripe credentials, bound from the <c>Stripe</c> configuration section.
/// </summary>
/// <remarks>
/// Unlike <c>Jwt:Key</c> / <c>Csrf:Key</c>, these are NOT validated at startup: checkout is a
/// feature, not a boot requirement, so the catalogue + cart still run on a fresh clone with no
/// Stripe keys. The keys are needed only when a payment endpoint is actually exercised (and the
/// integration tests fake the gateway, so they never need a real key). In production the values
/// come from Key Vault; in dev from user-secrets (<c>dotnet user-secrets set Stripe:SecretKey …</c>).
/// </remarks>
public sealed class StripeOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Stripe";

    /// <summary>Secret API key (<c>sk_test_…</c> in test mode). Used to create Checkout Sessions.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Webhook signing secret (<c>whsec_…</c>). Used to verify inbound webhook signatures.</summary>
    public string WebhookSigningSecret { get; set; } = string.Empty;
}
