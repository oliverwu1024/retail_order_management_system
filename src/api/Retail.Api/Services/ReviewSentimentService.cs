using Microsoft.EntityFrameworkCore;
using Retail.Api.Ai;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Services;

/// <summary>
/// Default <see cref="IReviewSentimentService"/> — scores one review via <see cref="ITextAnalyticsAdapter"/>
/// and writes the result back with a TRACKED load + SaveChanges (so the audit/stamp interceptors fire;
/// a set-based ExecuteUpdate would bypass them). Review is not on the audit-trail allowlist, so no
/// AuditLog row is written (intended — high volume).
/// </summary>
public sealed class ReviewSentimentService : IReviewSentimentService
{
    private readonly RetailDbContext _db;
    private readonly ITextAnalyticsAdapter _analytics;
    private readonly TimeProvider _timeProvider;

    public ReviewSentimentService(RetailDbContext db, ITextAnalyticsAdapter analytics, TimeProvider timeProvider)
    {
        _db = db;
        _analytics = analytics;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task ScoreAsync(Guid reviewId, CancellationToken ct)
    {
        // Tracked load (no AsNoTracking) so the property changes below persist on SaveChanges.
        Review? review = await _db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);
        if (review is null || review.ProcessedAt is not null)
        {
            return; // gone, or already scored — idempotent against a double-enqueue (submit + slow scan)
        }

        SentimentResult result = await _analytics.AnalyzeAsync(review.Body, ct);
        review.SentimentScore = result.Score;
        review.SentimentLabel = result.Label;
        review.ProcessedAt = _timeProvider.GetUtcNow();
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> GetUnscoredIdsAsync(int limit, CancellationToken ct) =>
        await _db.Reviews.AsNoTracking()
            .Where(r => r.ProcessedAt == null)
            .OrderBy(r => r.CreatedAt)
            .Take(limit)
            .Select(r => r.Id)
            .ToListAsync(ct);
}
