using System.Diagnostics;

namespace Retail.Api.Common.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  ApiResponse<T> — standard response envelope for every Retail API endpoint.
//
//  WHY AN ENVELOPE AT ALL?
//  -----------------------
//  Without a uniform shape, the React client has to branch on every endpoint:
//     - GET  /orders/123        →  body is an Order
//     - POST /orders (400)      →  body is ASP.NET Core's ProblemDetails
//     - POST /orders (422)      →  body is FluentValidation's error dict
//     - GET  /orders/999 (404)  →  body is empty
//  That's four parsers for one resource. With an envelope, the frontend has
//  ONE response interceptor that reads `success` and either unwraps `data`
//  or surfaces `errors`. This is the Recam-project convention and it matches
//  what CODING_STANDARDS.md calls out as the required shape.
//
//  WHY `init`-ONLY PROPERTIES (NOT `set`)?
//  ---------------------------------------
//  Response objects should be immutable once constructed — otherwise a
//  late-running filter or middleware could silently mutate the body. `init`
//  still lets us use object-initializer syntax inside the factory methods.
//
//  WHY `sealed class` AND NOT `record`?
//  ------------------------------------
//  `record` would give us value equality, which we never need for a response
//  envelope (two responses are "equal" only by accident — they're outputs, not
//  keys). `sealed class` is the conventional choice for DTOs in ASP.NET Core
//  and serializes cleanly with System.Text.Json's camelCase policy configured
//  globally in Program.cs.
//
//  WHY THE FACTORY METHODS (Ok / Fail) INSTEAD OF `new ApiResponse<T> { ... }`?
//  ---------------------------------------------------------------------------
//  Controllers call `ApiResponse<OrderDto>.Ok(order)` in one line. Without
//  factories, every controller would have to remember to set `Success = true`,
//  set `TraceId`, set `Timestamp`, etc. The factories enforce that invariant
//  in one place — controllers stay one-liners.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Standard response envelope wrapping every Retail API endpoint's output,
/// whether success or failure. Lets the React client parse all responses with
/// a single interceptor that branches on <see cref="Success"/>.
/// </summary>
/// <typeparam name="T">Payload type carried in <see cref="Data"/> on success.</typeparam>
public sealed class ApiResponse<T>
{
    /// <summary>
    /// <c>true</c> when the operation succeeded and <see cref="Data"/> is populated;
    /// <c>false</c> when <see cref="Errors"/> applies. This is the single flag the
    /// frontend interceptor branches on.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Payload returned on success (e.g., an <c>OrderDto</c>, a list of products).
    /// <c>null</c> on failure or for endpoints that have no return value
    /// (in which case prefer <see cref="ApiResponse"/> non-generic for clarity).
    /// </summary>
    public T? Data { get; init; }

    /// <summary>
    /// Human-readable summary safe to surface in a UI toast or banner.
    /// On success this is usually <c>null</c> (the UI knows what it just did);
    /// on failure this is the top-line message, with structured detail in
    /// <see cref="Errors"/>.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Structured error details. One response can carry many errors — e.g.,
    /// a FluentValidation result with failures on three different fields.
    /// <c>null</c> on success.
    /// </summary>
    public IReadOnlyList<ApiError>? Errors { get; init; }

    /// <summary>
    /// W3C trace ID from <see cref="Activity.Current"/>, populated automatically
    /// by the OpenTelemetry HTTP middleware (wired up in Program.cs). Used to
    /// correlate a frontend bug report — "the checkout failed, here's the
    /// TraceId" — with the backend distributed trace in Grafana Tempo / Jaeger.
    /// Empty string when no Activity is on the current async context (e.g., a
    /// background worker without an ambient trace).
    /// </summary>
    public string TraceId { get; init; } = Activity.Current?.TraceId.ToString() ?? string.Empty;

    /// <summary>
    /// UTC timestamp when the response was constructed. Cheap to include and
    /// invaluable for offline log forensics when correlating client-side and
    /// server-side timing.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Builds a success envelope wrapping the given payload.
    /// </summary>
    /// <param name="data">The successful result payload.</param>
    /// <param name="message">Optional human-readable summary for the UI.</param>
    public static ApiResponse<T> Ok(T data, string? message = null) => new()
    {
        Success = true,
        Data = data,
        Message = message,
    };

    /// <summary>
    /// Builds a failure envelope with a top-line message and optional structured errors.
    /// </summary>
    /// <param name="message">Top-line human-readable failure summary.</param>
    /// <param name="errors">Optional structured error list (e.g. validation failures).</param>
    public static ApiResponse<T> Fail(string message, IReadOnlyList<ApiError>? errors = null) => new()
    {
        Success = false,
        Message = message,
        Errors = errors,
    };
}

// ─────────────────────────────────────────────────────────────────────────────
//  Non-generic ApiResponse — convenience for void operations (DELETE,
//  POST that returns no body, etc.). Inheriting from ApiResponse<object?>
//  keeps the wire shape identical so the React client doesn't see two
//  different envelope structures.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Non-generic envelope for endpoints that don't return a payload. The wire
/// shape is identical to <see cref="ApiResponse{T}"/> with <c>data: null</c>.
/// </summary>
public sealed class ApiResponse
{
    /// <inheritdoc cref="ApiResponse{T}.Success"/>
    public bool Success { get; init; }

    /// <inheritdoc cref="ApiResponse{T}.Message"/>
    public string? Message { get; init; }

    /// <inheritdoc cref="ApiResponse{T}.Errors"/>
    public IReadOnlyList<ApiError>? Errors { get; init; }

    /// <inheritdoc cref="ApiResponse{T}.TraceId"/>
    public string TraceId { get; init; } = Activity.Current?.TraceId.ToString() ?? string.Empty;

    /// <inheritdoc cref="ApiResponse{T}.Timestamp"/>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Builds a success envelope with no payload.</summary>
    public static ApiResponse Ok(string? message = null) => new()
    {
        Success = true,
        Message = message,
    };

    /// <summary>Builds a failure envelope with a top-line message and optional errors.</summary>
    public static ApiResponse Fail(string message, IReadOnlyList<ApiError>? errors = null) => new()
    {
        Success = false,
        Message = message,
        Errors = errors,
    };
}

// ─────────────────────────────────────────────────────────────────────────────
//  ApiError — one structured error inside the Errors[] array.
//
//  WHY THREE FIELDS (Code, Message, Field)?
//  ----------------------------------------
//  * Code     — machine-readable. Frontend uses this to drive i18n strings and
//               conditional UI ("INVENTORY_INSUFFICIENT" → show the "out of
//               stock" CTA; "PAYMENT_DECLINED" → show retry-with-different-card
//               flow).
//  * Message  — human-readable English fallback. Never the source of truth for
//               localized UI — that's the frontend's job, keyed by Code.
//  * Field    — for validation errors only. The dotted path to the offending
//               input, e.g. "items[0].quantity". `null` for non-field errors
//               (concurrency conflict, payment declined, auth required, etc.).
//
//  WHY `required`?
//  ---------------
//  Code and Message must always be set. `required` makes the compiler enforce
//  that at construction time — you can't accidentally ship an error with no
//  code, which would break the frontend's branching logic.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Structured error detail carried in <see cref="ApiResponse{T}.Errors"/>.
/// Multiple errors per response are normal (e.g. a validation result with
/// failures on three different input fields).
/// </summary>
public sealed class ApiError
{
    /// <summary>
    /// Machine-readable error code in SCREAMING_SNAKE_CASE
    /// (e.g. <c>VALIDATION_REQUIRED</c>, <c>ORDER_NOT_FOUND</c>,
    /// <c>INVENTORY_INSUFFICIENT</c>, <c>PAYMENT_DECLINED</c>).
    /// The frontend keys i18n strings and conditional UI off this value.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// Human-readable English fallback message. NOT the source of truth for
    /// localized UI text — the frontend localizes by <see cref="Code"/>.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Dotted field path for validation errors (e.g. <c>"items[0].quantity"</c>).
    /// <c>null</c> for non-field errors like concurrency conflicts or payment
    /// declines that don't map to a single input.
    /// </summary>
    public string? Field { get; init; }
}
