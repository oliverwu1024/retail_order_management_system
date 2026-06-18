using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Retail.Api.Common.Helpers;
using Retail.Api.Common.Models;
using Retail.Api.Exceptions;

namespace Retail.Api.Middlewares;

// ─────────────────────────────────────────────────────────────────────────────
//  ExceptionMiddleware — single global handler for any unhandled exception
//  bubbling up from controllers, filters, validators, handlers, or background
//  work that ran on the request pipeline.
//
//  WHY A MIDDLEWARE INSTEAD OF try/catch IN EVERY CONTROLLER?
//  ----------------------------------------------------------
//  1. DRY. 50 controllers × 5 actions × the same boilerplate = 250 places to
//     get the JSON shape wrong.
//  2. Catches exceptions thrown by stuff controllers don't even see — model
//     binding, FluentValidation auto-validation, action filters, EF
//     SaveChanges interceptors. A try/catch inside an action never runs for
//     those because the exception happens before/after the action body.
//  3. Single owner of the wire format. If we change ApiResponse later, only
//     one file changes.
//  4. Logging discipline. Every unhandled exception flows through one place
//     where we log it with structured fields (TraceId, Path, Method, User).
//
//  WHY DO WE HIDE EXCEPTION DETAILS IN PRODUCTION?
//  -----------------------------------------------
//  Stack traces leak: SQL fragments (helps SQL injection probing), file paths
//  (reveal project structure), library versions (CVE targeting). In Development
//  we surface the full detail so we can debug; in Production we return a
//  generic message plus the TraceId so we can find the trace in Tempo/Jaeger.
//
//  WHY MAP SPECIFIC EXCEPTIONS TO SPECIFIC STATUS CODES?
//  -----------------------------------------------------
//  The frontend behaves differently per status:
//    401 → redirect to /login
//    404 → "not found" page or empty-state UI
//    409 → "your changes conflict, please refresh" toast with retry
//    500 → generic "something went wrong" + Sentry/error report flow
//  Returning 500 for everything would break all of that. Mapping is the
//  contract between server-side reality and client-side UX.
//
//  WHY CONVENTION-BASED MIDDLEWARE (constructor takes RequestDelegate)?
//  --------------------------------------------------------------------
//  Both this and IMiddleware work. Convention-based is what 95% of ASP.NET
//  Core docs show and what most interviewers will recognize. IMiddleware is
//  factory-instantiated per request (more testable, but the extra DI step
//  isn't needed here). We're optimizing for "explain it on a whiteboard."
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Global exception handler. Catches any unhandled exception bubbling up the
/// request pipeline, logs it with structured fields, maps it to the right HTTP
/// status code, and serializes a uniform <see cref="ApiResponse"/> envelope.
/// </summary>
/// <remarks>
/// Register as the OUTERMOST middleware in <c>Program.cs</c> so it can catch
/// everything downstream — including auth, routing, model binding, and the
/// MVC pipeline itself:
/// <code>app.UseMiddleware&lt;ExceptionMiddleware&gt;();</code>
/// </remarks>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IWebHostEnvironment _env;

    // System.Text.Json options used to serialize the failure envelope. We
    // mirror the ASP.NET Core defaults (camelCase, ignore nulls) so the wire
    // shape matches what controllers produce.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger,
        IWebHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    /// <summary>
    /// Entry point invoked by the ASP.NET Core pipeline for every request.
    /// Wraps the downstream pipeline in a try/catch.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            // Hand off to the rest of the pipeline. 99% of requests succeed
            // and return here without entering the catch.
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception ex)
    {
        // Map the exception to (statusCode, errorCode, userMessage).
        // Order matters — more specific types first. Add new cases here as
        // we introduce FluentValidation (ValidationException) and our own
        // domain exceptions in Retail.Api/Exceptions/ (NotFoundException,
        // BusinessRuleException, etc.).
        var (status, code, message) = ex switch
        {
            // Domain "not found" — surfaces the (safe) message, e.g. "Product 'x' not found".
            NotFoundException =>
                (StatusCodes.Status404NotFound, "NOT_FOUND", ex.Message),

            // Domain conflict — duplicate SKU/slug, business-rule violation, etc.
            ConflictException =>
                (StatusCodes.Status409Conflict, "CONFLICT", ex.Message),

            // A business precondition that needs state to check (e.g. reviewing a product you
            // never bought) — well-formed and authorized, but semantically unprocessable.
            BusinessRuleException =>
                (StatusCodes.Status422UnprocessableEntity, "BUSINESS_RULE", ex.Message),

            // An upstream dependency (the AI provider, etc.) failed after retries. The request was
            // valid; the dependency is unavailable — 503, not a client error.
            ExternalServiceException =>
                (StatusCodes.Status503ServiceUnavailable, "EXTERNAL_SERVICE_UNAVAILABLE", ex.Message),

            // Not enough available stock (OnHand − Reserved) to satisfy a reservation/purchase.
            OutOfStockException =>
                (StatusCodes.Status409Conflict, "INVENTORY_INSUFFICIENT", ex.Message),

            // A RowVersion-guarded ExecuteUpdate affected 0 rows — another writer won the race.
            ConcurrencyException =>
                (StatusCodes.Status409Conflict, "CONCURRENCY_CONFLICT", ex.Message),

            // EF Core raises this when an UPDATE/DELETE affects 0 rows because
            // another transaction beat us to it (optimistic concurrency via
            // a rowversion/timestamp column). 409 is the canonical mapping.
            DbUpdateConcurrencyException =>
                (StatusCodes.Status409Conflict,
                 "CONCURRENCY_CONFLICT",
                 "The record was modified by another user. Please refresh and try again."),

            // A SQL Server unique/PK violation (2601/2627) — e.g. two concurrent first-adds
            // racing the one-open-cart-per-owner index. A conflict, not a 500: the winning row
            // now exists, so the client can simply retry. (EF surfaces this as a plain
            // DbUpdateException, distinct from the 0-rows DbUpdateConcurrencyException above.)
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } =>
                (StatusCodes.Status409Conflict,
                 "CONFLICT",
                 "That action conflicted with a concurrent change. Please try again."),

            // Thrown by [Authorize] policy handlers and by any code path that
            // explicitly rejects an authenticated principal. Distinct from
            // a missing-token case, which the auth middleware handles with
            // 401 before we ever get here.
            UnauthorizedAccessException =>
                (StatusCodes.Status403Forbidden,
                 "FORBIDDEN",
                 "You do not have permission to perform this action."),

            // BCL exception conventionally used for "lookup by id returned
            // nothing." We'll add a richer NotFoundException in
            // Retail.Api/Exceptions/ later and add a case for it above this
            // one (more specific first).
            KeyNotFoundException =>
                (StatusCodes.Status404NotFound,
                 "NOT_FOUND",
                 "The requested resource was not found."),

            // Catch-all. Anything we didn't anticipate is a 500. The actual
            // exception type, message, and stack trace are written to logs;
            // the client only sees the generic message + TraceId.
            _ =>
                (StatusCodes.Status500InternalServerError,
                 "INTERNAL_ERROR",
                 "An unexpected error occurred. Please contact support with the trace id."),
        };

        // Structured logging. Serilog reads these properties and indexes them
        // (in Seq/Elastic) so we can query "all CONCURRENCY_CONFLICT on
        // /api/orders in the last hour" without grepping raw text. Level is
        // Error for 5xx, Warning for 4xx — keeps the on-call signal:noise
        // ratio sane.
        var logLevel = status >= 500 ? LogLevel.Error : LogLevel.Warning;
        _logger.Log(
            logLevel,
            ex,
            "Unhandled exception {ErrorCode} on {Method} {Path} → {StatusCode}",
            code,
            context.Request.Method,
            // Mask any bearer-bearing path segment (e.g. the guest order-lookup session id), which
            // 404s on every success-page poll and would otherwise leak here. (P2-S2)
            LogPathSanitizer.Sanitize(context.Request.Path.Value),
            status);

        // Build the failure envelope. In Development we include the exception
        // detail (type + message + stack) as an extra ApiError so the dev
        // tools tab shows it; in Production we omit it entirely.
        var errors = _env.IsDevelopment()
            ? new List<ApiError>
            {
                new() { Code = code, Message = ex.Message, Field = null },
                new() { Code = "STACK_TRACE", Message = ex.ToString(), Field = null },
            }
            : new List<ApiError>
            {
                new() { Code = code, Message = message, Field = null },
            };

        var envelope = ApiResponse.Fail(message, errors);

        // Write the response. We must NOT have written to the body already —
        // if downstream middleware partially wrote a response and then threw,
        // the headers are already flushed and we can't change the status code.
        // The "if (HasStarted) bail" guard below covers that edge case.
        if (context.Response.HasStarted)
        {
            _logger.LogWarning(
                "Response already started; ExceptionMiddleware cannot rewrite the body for {ErrorCode}.",
                code);
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";

        await JsonSerializer.SerializeAsync(context.Response.Body, envelope, JsonOptions, context.RequestAborted);
    }
}
