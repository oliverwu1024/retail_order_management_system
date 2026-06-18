using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Retail.Api.Services;

namespace Retail.Api.HostedServices;

/// <summary>
/// Background sentiment scorer (REQUIREMENTS §6.3), modelled on <see cref="CartExpirySweeper"/>. Runs
/// two loops: a FAST drain of <see cref="ReviewSentimentQueue"/> (near-real-time scoring of newly
/// submitted reviews) and a SLOW periodic re-scan that re-queues any <c>ProcessedAt IS NULL</c> review
/// — the retry path for Azure failures AND the restart-safety net for the in-process channel.
/// </summary>
/// <remarks>
/// A hosted service is a SINGLETON, so it resolves the scoped <see cref="IReviewSentimentService"/>
/// from a fresh DI scope per item. Scoring is idempotent (skips already-scored reviews), so a review
/// enqueued by both the submit path and the slow scan is scored once.
/// </remarks>
public sealed class ReviewSentimentHostedService : BackgroundService
{
    private static readonly TimeSpan RescanInterval = TimeSpan.FromMinutes(5);
    private const int RescanBatch = 100;

    private readonly ReviewSentimentQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ReviewSentimentHostedService> _logger;

    public ReviewSentimentHostedService(
        ReviewSentimentQueue queue,
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<ReviewSentimentHostedService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(DrainQueueAsync(stoppingToken), SlowRescanAsync(stoppingToken));

    // Fast path: score reviews as they're enqueued on submit.
    private async Task DrainQueueAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (Guid reviewId in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                await ScoreOneAsync(reviewId, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    // Slow path: periodically re-queue anything still unscored (retry + restart recovery).
    private async Task SlowRescanAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RescanInterval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                    IReviewSentimentService sentiment = scope.ServiceProvider.GetRequiredService<IReviewSentimentService>();
                    IReadOnlyList<Guid> pending = await sentiment.GetUnscoredIdsAsync(RescanBatch, stoppingToken);
                    foreach (Guid id in pending)
                    {
                        _queue.Enqueue(id);
                    }
                    if (pending.Count > 0)
                    {
                        _logger.LogInformation("Re-queued {Count} unscored review(s) for sentiment scoring.", pending.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Sentiment re-scan failed; will retry next interval.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task ScoreOneAsync(Guid reviewId, CancellationToken ct)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
            IReviewSentimentService sentiment = scope.ServiceProvider.GetRequiredService<IReviewSentimentService>();
            await sentiment.ScoreAsync(reviewId, ct);
        }
        catch (Exception ex)
        {
            // A failed scoring must not kill the drain loop — the review keeps ProcessedAt=null and
            // the slow re-scan retries it later.
            _logger.LogWarning(ex, "Sentiment scoring failed for review {ReviewId}; will retry via the slow scan.", reviewId);
        }
    }
}
