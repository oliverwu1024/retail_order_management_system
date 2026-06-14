using System.Text.Json;
using Retail.Api.Common.Constants;
using Retail.Api.Common.Helpers;
using Retail.Api.Common.Models;
using Retail.Api.Identity;

namespace Retail.Api.Middlewares;

/// <summary>
/// Enforces the signed double-submit CSRF check (ADR-0007) on every state-changing
/// request. Safe (read-only) methods pass through untouched.
/// </summary>
/// <remarks>
/// <para>
/// The rule: for any non-safe method, the request must carry a <c>csrf</c> cookie
/// AND an <c>X-CSRF-Token</c> header that (a) equals the cookie value and (b) is a
/// validly-signed token. Failing any of those is a 403.
/// </para>
/// <para>
/// This is defense-in-depth layered on top of <c>SameSite=Strict</c> (which is the
/// primary CSRF defense — a cross-site request never carries the auth cookie at
/// all). The bootstrap step (<c>GET /auth/csrf</c>) is a safe method, so it is
/// exempt and can hand the SPA its first token.
/// </para>
/// </remarks>
public sealed class CsrfMiddleware
{
    private static readonly HashSet<string> SafeMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET",
        "HEAD",
        "OPTIONS",
        "TRACE",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly RequestDelegate _next;

    public CsrfMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    // ICsrfTokenService is resolved per-request via method injection (convention
    // middleware supports extra InvokeAsync parameters from DI).
    public async Task InvokeAsync(HttpContext context, ICsrfTokenService csrf)
    {
        if (!SafeMethods.Contains(context.Request.Method))
        {
            string? cookie = context.Request.Cookies[AuthConstants.CsrfCookie];
            string header = context.Request.Headers[AuthConstants.CsrfHeader].ToString();

            bool valid =
                !string.IsNullOrEmpty(cookie)
                && !string.IsNullOrEmpty(header)
                && SecureTokens.FixedTimeEquals(cookie, header)
                && csrf.Validate(cookie);

            if (!valid)
            {
                await WriteForbiddenAsync(context);
                return;
            }
        }

        await _next(context);
    }

    private static async Task WriteForbiddenAsync(HttpContext context)
    {
        var envelope = ApiResponse.Fail(
            "CSRF validation failed. Refresh the page and try again.",
            new List<ApiError>
            {
                new() { Code = "CSRF_VALIDATION_FAILED", Message = "Missing or invalid CSRF token.", Field = null },
            });

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, envelope, JsonOptions);
    }
}
