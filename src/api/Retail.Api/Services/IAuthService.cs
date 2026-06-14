using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Identity;

namespace Retail.Api.Services;

/// <summary>
/// Orchestrates authentication: registration, login, refresh-token rotation, and
/// logout. Deliberately HTTP-agnostic — it returns an <see cref="AuthResult"/>
/// carrying raw tokens, and the controller is responsible for turning those into
/// Set-Cookie headers. That split keeps <c>HttpContext</c> out of the service and
/// makes every path unit-testable without a web host.
/// </summary>
public interface IAuthService
{
    /// <summary>Creates a customer account, assigns the Customer role, and issues an initial token pair.</summary>
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct);

    /// <summary>Verifies credentials (honouring lockout) and issues a token pair.</summary>
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct);

    /// <summary>Validates and rotates a refresh token, detecting replay of a revoked token.</summary>
    Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken ct);

    /// <summary>Revokes the presented refresh token. Idempotent — a missing/unknown token is a no-op.</summary>
    Task LogoutAsync(string? refreshToken, CancellationToken ct);

    /// <summary>Loads the current user's profile for <c>GET /auth/me</c>, or null if the id no longer resolves.</summary>
    Task<AuthUserDto?> GetCurrentUserAsync(string userId, CancellationToken ct);
}
