using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail.Api.Common.Models;

namespace Retail.Api.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  HealthController — a controller-based heartbeat endpoint.
//
//  WHY THIS WHEN WE ALREADY HAVE /health/live AND /health/ready?
//  -------------------------------------------------------------
//  The MapHealthChecks endpoints return the built-in ASP.NET Core health-check
//  JSON shape ({ "status": "Healthy", "entries": {...} }) — useful for k8s /
//  Container Apps probes. This controller returns the project's STANDARD
//  ApiResponse envelope. That makes it the right end-to-end smoke test target:
//  if hitting /api/health returns a properly-shaped ApiResponse, we've
//  verified the full MVC + JSON serialization + envelope contract works.
//
//  WHY [AllowAnonymous]?
//  ---------------------
//  Health endpoints must be reachable without a token. Otherwise the load
//  balancer / readiness probe can never confirm the service is up. We mark
//  it explicitly even though no [Authorize] policy is set globally yet —
//  defensive, so an "AddDefaultPolicy(...)" added later doesn't silently
//  start requiring auth on this endpoint.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Heartbeat endpoint returning a uniform <see cref="ApiResponse{T}"/>
/// envelope. Used for end-to-end smoke testing the MVC + serialization
/// pipeline, and as a simple "is the API alive" probe for clients.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class HealthController : ControllerBase
{
    /// <summary>
    /// GET /api/health — returns a successful <see cref="ApiResponse{T}"/>
    /// wrapping a small heartbeat payload (service name, environment, UTC
    /// timestamp). Always 200.
    /// </summary>
    [HttpGet]
    public ActionResult<ApiResponse<HealthPayload>> Get(
        [FromServices] IWebHostEnvironment env)
    {
        var payload = new HealthPayload
        {
            Service = "Retail.Api",
            Environment = env.EnvironmentName,
            UtcNow = DateTimeOffset.UtcNow,
        };

        return Ok(ApiResponse<HealthPayload>.Ok(payload, "Service is healthy"));
    }
}

/// <summary>
/// Payload shape returned by <see cref="HealthController.Get"/>.
/// Kept in this file because it's the only consumer; will migrate to
/// <c>DTOs/Responses/</c> if it's ever reused.
/// </summary>
public sealed class HealthPayload
{
    /// <summary>Service identifier — matches the OpenTelemetry resource name.</summary>
    public required string Service { get; init; }

    /// <summary>ASP.NET Core environment name (Development, Staging, Production).</summary>
    public required string Environment { get; init; }

    /// <summary>UTC timestamp at which the response was constructed.</summary>
    public required DateTimeOffset UtcNow { get; init; }
}
