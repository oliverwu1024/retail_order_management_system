using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// A single issued refresh token. Persisted (as a hash) so refresh tokens are
/// revocable and rotation can be enforced — the core of ADR-0007's refresh-token
/// rotation with reuse detection.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only the hash is stored.</b> <see cref="TokenHash"/> is the SHA-256 of the
/// opaque token that was placed in the cookie; the raw token exists only in the
/// client's cookie. A database leak therefore yields no usable token.
/// </para>
/// <para>
/// <b>Lifecycle.</b> A token starts active (<see cref="RevokedAt"/> null,
/// <see cref="ExpiresAt"/> in the future). On a successful refresh it is rotated:
/// <see cref="RevokedAt"/> is stamped, <see cref="ReplacedByTokenHash"/> points at
/// the successor, and <see cref="ReasonRevoked"/> records why. Presenting an
/// already-revoked token is treated as reuse (theft) and triggers revocation of
/// the user's whole active set — see <c>AuthService.RefreshAsync</c>.
/// </para>
/// </remarks>
public class RefreshToken : IAuditableEntity
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the owning <see cref="ApplicationUser"/> (Identity's string id).</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>Navigation to the owning user.</summary>
    public ApplicationUser? User { get; set; }

    /// <summary>SHA-256 (hex) of the opaque token. Indexed unique; the lookup key on refresh.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>When the token stops being valid even if never used.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When the token was revoked (rotated, logged out, or reuse-revoked). Null while active.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Hash of the token that replaced this one on rotation. Null if not rotated.</summary>
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>Why the token was revoked: <c>rotated</c>, <c>logout</c>, <c>reuse-detected</c>, <c>expired</c>.</summary>
    public string? ReasonRevoked { get; set; }

    /// <summary>Convenience flag: a token is revoked once <see cref="RevokedAt"/> is set. (Expiry needs the clock, so it is checked in the service.)</summary>
    public bool IsRevoked => RevokedAt is not null;

    // ── IAuditableEntity (stamped by AuditingInterceptor) ────────────────────
    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }
    /// <inheritdoc />
    public string? CreatedBy { get; set; }
    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }
    /// <inheritdoc />
    public string? UpdatedBy { get; set; }
}
