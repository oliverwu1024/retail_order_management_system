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
    public async Task<IReadOnlyList<RefreshToken>> ListActiveByUserAsync(string userId, DateTimeOffset now, CancellationToken ct) =>
        await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken ct) =>
        await _db.SaveChangesAsync(ct);
}
