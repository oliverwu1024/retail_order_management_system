using Retail.Api.Domain.Entities;

namespace Retail.Api.Repositories;

/// <summary>
/// Data access for <see cref="RefreshToken"/> rows. Pure persistence — the
/// rotation/reuse policy lives in <c>AuthService</c>, not here (Repositories never
/// hold business logic; see CODING_STANDARDS §三层依赖规则).
/// </summary>
public interface IRefreshTokenRepository
{
    /// <summary>Stages a new token for insert (call <see cref="SaveChangesAsync"/> to persist).</summary>
    Task AddAsync(RefreshToken token, CancellationToken ct);

    /// <summary>Finds a token by its stored hash, or null. The lookup key on refresh/logout.</summary>
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct);

    /// <summary>Lists a user's not-yet-revoked tokens — the set revoked wholesale on reuse detection.</summary>
    Task<IReadOnlyList<RefreshToken>> ListNotRevokedByUserAsync(string userId, CancellationToken ct);

    /// <summary>Persists all staged changes (inserts + property updates tracked on loaded entities).</summary>
    Task SaveChangesAsync(CancellationToken ct);
}
