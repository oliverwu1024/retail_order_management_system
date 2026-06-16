using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail.Api.Payments;
using Stripe;

namespace Retail.Api.Controllers;

/// <summary>
/// Stripe payment webhooks (Story 2.2). A server-to-server endpoint authenticated by the
/// <c>Stripe-Signature</c> header — NOT cookies — so it is <see cref="AllowAnonymousAttribute"/>
/// and CSRF-exempt (the path is allowlisted in <c>CsrfMiddleware</c>).
/// </summary>
[ApiController]
[Route("api/v1/payments")]
[AllowAnonymous]
public sealed class PaymentsController : ControllerBase
{
    private readonly IStripeWebhookService _webhook;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(IStripeWebhookService webhook, ILogger<PaymentsController> logger)
    {
        _webhook = webhook;
        _logger = logger;
    }

    /// <summary>Receives Stripe webhook events (signature-verified + idempotent).</summary>
    [HttpPost("stripe/webhook")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StripeWebhook(CancellationToken ct)
    {
        // Read the RAW body — the signature is an HMAC over the exact bytes Stripe sent, so it
        // must not go through model binding.
        using var reader = new StreamReader(Request.Body);
        string payload = await reader.ReadToEndAsync(ct);
        string signature = Request.Headers["Stripe-Signature"].ToString();

        try
        {
            await _webhook.HandleAsync(payload, signature, ct);
            return Ok();
        }
        catch (StripeException ex)
        {
            // Bad/missing signature or malformed event → 400 (an unverifiable payload should not
            // be retried forever). Genuine processing failures bubble to the global handler as
            // 500, which Stripe DOES retry.
            _logger.LogWarning(ex, "Rejected a Stripe webhook: {Message}", ex.Message);
            return BadRequest();
        }
    }
}
