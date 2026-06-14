using Retail.Api.Common.Models;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Identity;

/// <summary>
/// Why a failure was returned by <c>AuthService</c>. The controller maps
/// each kind to an HTTP status + error code — keeping HTTP concerns out of the
/// service layer (which never touches <c>HttpContext</c>).
/// </summary>
public enum AuthError
{
    /// <summary>No error — the result succeeded.</summary>
    None = 0,

    /// <summary>Wrong email/password, or an unknown email. Deliberately indistinguishable to the caller.</summary>
    InvalidCredentials,

    /// <summary>Registration email already belongs to an account.</summary>
    EmailAlreadyTaken,

    /// <summary>Password failed Identity's policy (length/complexity). Carries field-level detail.</summary>
    WeakPassword,

    /// <summary>Refresh token missing, unknown, expired, or replayed (reuse).</summary>
    InvalidRefreshToken,

    /// <summary>Account temporarily locked after too many failed attempts.</summary>
    LockedOut,
}

/// <summary>
/// The freshly-minted credentials for a successful auth operation. The
/// <see cref="RefreshToken"/> here is the RAW token (what goes in the cookie) —
/// only its hash is persisted.
/// </summary>
public sealed record AuthTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    AuthUserDto User);

/// <summary>
/// Outcome of an auth operation: either success (with <see cref="Tokens"/>) or a
/// typed failure. Modeled as a result rather than exceptions because "wrong
/// password" and "email taken" are expected control flow, not exceptional —
/// throwing for them would be both slower and semantically wrong.
/// </summary>
public sealed class AuthResult
{
    /// <summary><c>true</c> when the operation succeeded; <see cref="Tokens"/> is then non-null.</summary>
    public bool Succeeded { get; private init; }

    /// <summary>The minted tokens + user profile on success; null on failure.</summary>
    public AuthTokens? Tokens { get; private init; }

    /// <summary>The failure kind on failure; <see cref="AuthError.None"/> on success.</summary>
    public AuthError Error { get; private init; }

    /// <summary>Human-readable failure summary (safe for a UI toast).</summary>
    public string? Message { get; private init; }

    /// <summary>Optional field-level errors (e.g. Identity password-policy failures) for a 422 body.</summary>
    public IReadOnlyList<ApiError>? FieldErrors { get; private init; }

    /// <summary>Builds a success result.</summary>
    public static AuthResult Success(AuthTokens tokens) => new()
    {
        Succeeded = true,
        Tokens = tokens,
        Error = AuthError.None,
    };

    /// <summary>Builds a typed failure result.</summary>
    public static AuthResult Fail(AuthError error, string message, IReadOnlyList<ApiError>? fieldErrors = null) => new()
    {
        Succeeded = false,
        Error = error,
        Message = message,
        FieldErrors = fieldErrors,
    };
}
