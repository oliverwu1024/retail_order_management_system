namespace Retail.Api.Services;

/// <summary>Scores review sentiment via Azure AI Language (Phase 4, Story 4.3). Scoped — the hosted service resolves it per item.</summary>
public interface IReviewSentimentService
{
    /// <summary>Scores one review and persists the result. Idempotent — a no-op if the review is gone or already scored.</summary>
    Task ScoreAsync(Guid reviewId, CancellationToken ct);

    /// <summary>Ids of reviews still awaiting scoring (<c>ProcessedAt IS NULL</c>), oldest first — the slow re-scan source.</summary>
    Task<IReadOnlyList<Guid>> GetUnscoredIdsAsync(int limit, CancellationToken ct);
}
