using System.Security.Claims;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Retail.Api.Common.Constants;
using Retail.Api.Common.Models;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Identity;
using Retail.Api.Services;

namespace Retail.Api.Controllers;

/// <summary>
/// Cookie-based authentication endpoints (ADR-0007). On success, credentials are
/// returned ONLY as Set-Cookie headers — the response body never carries a token.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ICsrfTokenService _csrfTokenService;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly bool _secureCookies;

    public AuthController(
        IAuthService authService,
        ICsrfTokenService csrfTokenService,
        IValidator<RegisterRequest> registerValidator,
        IValidator<LoginRequest> loginValidator,
        IOptions<AuthSettings> authSettings)
    {
        _authService = authService;
        _csrfTokenService = csrfTokenService;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _secureCookies = authSettings.Value.SecureCookies;
    }

    /// <summary>
    /// Issues a CSRF token cookie. The SPA calls this once on load (and after auth)
    /// to obtain the value it must echo in the <c>X-CSRF-Token</c> header on every
    /// state-changing request.
    /// </summary>
    [HttpGet("csrf")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public IActionResult GetCsrfToken()
    {
        AuthCookies.WriteCsrf(Response, _csrfTokenService.Issue(), _secureCookies);
        return Ok(ApiResponse.Ok("CSRF token issued."));
    }

    /// <summary>Registers a new customer and signs them in (cookies set on success).</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken ct)
    {
        ValidationResult validation = await _registerValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return UnprocessableEntity(ApiResponse.Fail("Validation failed.", ToApiErrors(validation)));
        }

        AuthResult result = await _authService.RegisterAsync(request, ct);
        return HandleAuthResult(result);
    }

    /// <summary>Verifies credentials and signs the user in (cookies set on success).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        ValidationResult validation = await _loginValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return UnprocessableEntity(ApiResponse.Fail("Validation failed.", ToApiErrors(validation)));
        }

        AuthResult result = await _authService.LoginAsync(request, ct);
        return HandleAuthResult(result);
    }

    /// <summary>
    /// Rotates the refresh token carried in the cookie and re-issues the pair.
    /// On any failure the auth cookies are cleared so the client falls back to login.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        string? refreshToken = Request.Cookies[AuthConstants.RefreshTokenCookie];
        AuthResult result = await _authService.RefreshAsync(refreshToken, ct);

        if (!result.Succeeded)
        {
            AuthCookies.Clear(Response, _secureCookies);
        }

        return HandleAuthResult(result);
    }

    /// <summary>Revokes the refresh token server-side and clears all auth cookies.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        string? refreshToken = Request.Cookies[AuthConstants.RefreshTokenCookie];
        await _authService.LogoutAsync(refreshToken, ct);
        AuthCookies.Clear(Response, _secureCookies);
        return Ok(ApiResponse.Ok("Logged out."));
    }

    /// <summary>Returns the current authenticated user's profile. Proves the cookie→JWT round-trip.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<AuthUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        string? userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(ApiResponse.Fail("Not authenticated."));
        }

        AuthUserDto? user = await _authService.GetCurrentUserAsync(userId, ct);
        if (user is null)
        {
            return Unauthorized(ApiResponse.Fail("Not authenticated."));
        }

        return Ok(ApiResponse<AuthUserDto>.Ok(user));
    }

    // ── mapping helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Turns a service <see cref="AuthResult"/> into an HTTP response: on success,
    /// writes the access/refresh/CSRF cookies and returns the user; on failure,
    /// maps the typed <see cref="AuthError"/> to a status code + error body.
    /// </summary>
    private IActionResult HandleAuthResult(AuthResult result)
    {
        if (result.Succeeded && result.Tokens is not null)
        {
            AuthTokens tokens = result.Tokens;
            AuthCookies.WriteAccessToken(Response, tokens.AccessToken, tokens.AccessTokenExpiresAt, _secureCookies);
            AuthCookies.WriteRefreshToken(Response, tokens.RefreshToken, tokens.RefreshTokenExpiresAt, _secureCookies);
            // Re-issue a fresh CSRF token alongside the new session.
            AuthCookies.WriteCsrf(Response, _csrfTokenService.Issue(), _secureCookies);
            return Ok(ApiResponse<AuthUserDto>.Ok(tokens.User));
        }

        (int status, string code) = MapError(result.Error);
        string message = result.Message ?? "Authentication failed.";
        IReadOnlyList<ApiError> errors = result.FieldErrors
            ?? new List<ApiError> { new() { Code = code, Message = message, Field = null } };

        return StatusCode(status, ApiResponse.Fail(message, errors));
    }

    // Maps the service's typed error to (HTTP status, machine-readable code).
    private static (int Status, string Code) MapError(AuthError error) => error switch
    {
        AuthError.InvalidCredentials => (StatusCodes.Status401Unauthorized, "UNAUTHORIZED"),
        AuthError.InvalidRefreshToken => (StatusCodes.Status401Unauthorized, "UNAUTHORIZED"),
        AuthError.LockedOut => (StatusCodes.Status423Locked, "ACCOUNT_LOCKED"),
        AuthError.EmailAlreadyTaken => (StatusCodes.Status409Conflict, "CONFLICT"),
        AuthError.WeakPassword => (StatusCodes.Status422UnprocessableEntity, "VALIDATION_ERROR"),
        _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR"),
    };

    private static IReadOnlyList<ApiError> ToApiErrors(ValidationResult validation) =>
        validation.Errors
            .Select(failure => new ApiError
            {
                Code = "VALIDATION_ERROR",
                Message = failure.ErrorMessage,
                Field = failure.PropertyName,
            })
            .ToList();
}
