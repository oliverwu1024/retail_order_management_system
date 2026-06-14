using Microsoft.EntityFrameworkCore;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IRefreshTokenRepository"/>.
/// </summary>
public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly RetailDbContext _db;

    public RefreshTokenRepository(RetailDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task AddAsync(RefreshToken token, CancellationToken ct) =>
        await _db.RefreshTokens.AddAsync(token, ct);

    /// <inheritdoc />
    public async Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct) =>
        await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RefreshToken>> ListNotRevokedByUserAsync(string userId, CancellationToken ct) =>
        // Filter on RevokedAt only (not expiry): on reuse we revoke the user's whole
        // live set, and re-revoking an already-expired token is harmless. This also
        // keeps the query provider-portable — SQLite can't translate a DateTimeOffset
        // ">" comparison, which a server-side expiry filter would require.
        await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken ct) =>
        await _db.SaveChangesAsync(ct);
}
