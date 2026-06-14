using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Retail.Api.Common.Constants;
using Retail.Api.Common.Helpers;
using Retail.Api.Common.Models;
using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Identity;
using Retail.Api.Repositories;

namespace Retail.Api.Services;

/// <summary>
/// Default <see cref="IAuthService"/>. Uses Identity's <see cref="UserManager{T}"/>
/// / <see cref="SignInManager{T}"/> for the user/password side and
/// <see cref="IRefreshTokenRepository"/> for the rotation side; the JWT itself is
/// minted by <see cref="IJwtService"/>.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IJwtService _jwtService;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly TimeProvider _timeProvider;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IJwtService jwtService,
        IRefreshTokenRepository refreshTokens,
        TimeProvider timeProvider,
        IOptions<JwtOptions> jwtOptions,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtService = jwtService;
        _refreshTokens = refreshTokens;
        _timeProvider = timeProvider;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        // Pre-check for a friendly "email taken" rather than a raw Identity error.
        // (Identity still enforces uniqueness at insert, closing the check-then-act race.)
        ApplicationUser? existing = await _userManager.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            return AuthResult.Fail(AuthError.EmailAlreadyTaken, "An account with that email already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
        };

        IdentityResult created = await _userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            // Covers password-policy failures and any duplicate that slipped past
            // the pre-check; the structured errors carry the real reason.
            return AuthResult.Fail(AuthError.WeakPassword, "Registration failed.", MapIdentityErrors(created));
        }

        await _userManager.AddToRoleAsync(user, Roles.Customer);
        _logger.LogInformation("Registered new customer {UserId}", user.Id);

        return await IssueTokensAsync(user, ct);
    }

    /// <inheritdoc />
    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        ApplicationUser? user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
        {
            // Same message + kind as a wrong password — never reveal which.
            return AuthResult.Fail(AuthError.InvalidCredentials, "Invalid email or password.");
        }

        // lockoutOnFailure: true makes this honour the lockout policy configured in
        // Program.cs (5 failures → 15-minute lockout).
        SignInResult signIn = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (signIn.IsLockedOut)
        {
            _logger.LogWarning("Login blocked: account {UserId} is locked out", user.Id);
            return AuthResult.Fail(AuthError.LockedOut, "Account temporarily locked due to repeated failed attempts. Try again later.");
        }

        if (!signIn.Succeeded)
        {
            return AuthResult.Fail(AuthError.InvalidCredentials, "Invalid email or password.");
        }

        return await IssueTokensAsync(user, ct);
    }

    /// <inheritdoc />
    public async Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return AuthResult.Fail(AuthError.InvalidRefreshToken, "No refresh token provided.");
        }

        string hash = SecureTokens.Sha256(refreshToken);
        RefreshToken? stored = await _refreshTokens.GetByHashAsync(hash, ct);
        if (stored is null)
        {
            return AuthResult.Fail(AuthError.InvalidRefreshToken, "Session is no longer valid. Please sign in again.");
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        // ── Reuse detection ──────────────────────────────────────────────────
        // A token that was already revoked (i.e. already rotated away) is being
        // presented again. That is the fingerprint of a stolen, replayed token.
        // Response: revoke EVERY active token for this user — a global logout that
        // makes the thief's copy (and any other live session) worthless.
        if (stored.RevokedAt is not null)
        {
            IReadOnlyList<RefreshToken> live = await _refreshTokens.ListNotRevokedByUserAsync(stored.UserId, ct);
            foreach (RefreshToken token in live)
            {
                token.RevokedAt = now;
                token.ReasonRevoked = "reuse-detected";
            }

            await _refreshTokens.SaveChangesAsync(ct);
            _logger.LogWarning(
                "Refresh-token reuse detected for user {UserId}; revoked {Count} live token(s)",
                stored.UserId, live.Count);

            return AuthResult.Fail(AuthError.InvalidRefreshToken, "Session is no longer valid. Please sign in again.");
        }

        if (stored.ExpiresAt <= now)
        {
            stored.RevokedAt = now;
            stored.ReasonRevoked = "expired";
            await _refreshTokens.SaveChangesAsync(ct);
            return AuthResult.Fail(AuthError.InvalidRefreshToken, "Session expired. Please sign in again.");
        }

        ApplicationUser? user = await _userManager.FindByIdAsync(stored.UserId);
        if (user is null)
        {
            // User deleted but a token lingered — treat as invalid.
            return AuthResult.Fail(AuthError.InvalidRefreshToken, "Session is no longer valid. Please sign in again.");
        }

        // Valid + active → rotate: issue a successor and revoke this one, linked.
        return await IssueTokensAsync(user, ct, replacing: stored);
    }

    /// <inheritdoc />
    public async Task LogoutAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return;
        }

        string hash = SecureTokens.Sha256(refreshToken);
        RefreshToken? stored = await _refreshTokens.GetByHashAsync(hash, ct);
        if (stored is not null && stored.RevokedAt is null)
        {
            stored.RevokedAt = _timeProvider.GetUtcNow();
            stored.ReasonRevoked = "logout";
            await _refreshTokens.SaveChangesAsync(ct);
        }
    }

    /// <inheritdoc />
    public async Task<AuthUserDto?> GetCurrentUserAsync(string userId, CancellationToken ct)
    {
        ApplicationUser? user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return null;
        }

        IList<string> roles = await _userManager.GetRolesAsync(user);
        return ToDto(user, roles);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Mints a fresh access+refresh pair for the user, persisting the new refresh
    /// token's hash. When <paramref name="replacing"/> is supplied (a rotation),
    /// the predecessor is revoked and linked to its successor in the same save.
    /// </summary>
    private async Task<AuthResult> IssueTokensAsync(ApplicationUser user, CancellationToken ct, RefreshToken? replacing = null)
    {
        IList<string> roles = await _userManager.GetRolesAsync(user);
        (string accessToken, DateTimeOffset accessExpiresAt) = _jwtService.CreateAccessToken(user, roles);

        DateTimeOffset now = _timeProvider.GetUtcNow();
        string rawRefresh = SecureTokens.NewToken();
        string refreshHash = SecureTokens.Sha256(rawRefresh);
        DateTimeOffset refreshExpiresAt = now.AddDays(_jwtOptions.RefreshTokenDays);

        var newToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAt = refreshExpiresAt,
        };
        await _refreshTokens.AddAsync(newToken, ct);

        if (replacing is not null)
        {
            replacing.RevokedAt = now;
            replacing.ReasonRevoked = "rotated";
            replacing.ReplacedByTokenHash = refreshHash;
        }

        await _refreshTokens.SaveChangesAsync(ct);

        var tokens = new AuthTokens(accessToken, accessExpiresAt, rawRefresh, refreshExpiresAt, ToDto(user, roles));
        return AuthResult.Success(tokens);
    }

    private static AuthUserDto ToDto(ApplicationUser user, IList<string> roles) =>
        new(
            user.Id,
            user.Email ?? string.Empty,
            user.DisplayName ?? user.Email ?? string.Empty,
            roles.ToList());

    private static IReadOnlyList<ApiError> MapIdentityErrors(IdentityResult result) =>
        result.Errors
            .Select(e => new ApiError
            {
                Code = e.Code,
                Message = e.Description,
                Field = FieldForIdentityCode(e.Code),
            })
            .ToList();

    // Best-effort mapping of Identity's error codes to the offending input field,
    // so the SPA can attach the message to the right form control.
    private static string? FieldForIdentityCode(string code)
    {
        if (code.Contains("Password", StringComparison.OrdinalIgnoreCase))
        {
            return "password";
        }

        if (code.Contains("Email", StringComparison.OrdinalIgnoreCase)
            || code.Contains("UserName", StringComparison.OrdinalIgnoreCase))
        {
            return "email";
        }

        return null;
    }
}
