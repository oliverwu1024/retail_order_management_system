using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Ai;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.Exceptions;
using Retail.Api.Services;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// Async review-sentiment scoring (Phase 4, Story 4.3) on real SQL Server, via the hermetic
/// StubTextAnalyticsAdapter (keyword scorer). Verifies the scorer populates the columns, is
/// idempotent against a double-enqueue, and that the slow-scan query (ProcessedAt IS NULL) picks up
/// pending reviews and drops them once scored.
/// </summary>
[Collection("api")]
public class ReviewSentimentServiceTests
{
    private readonly ApiFactory _factory;

    public ReviewSentimentServiceTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ScoreAsync_PopulatesSentiment()
    {
        Guid reviewId = await SeedReviewAsync("Great product, I love it. Excellent quality.");

        await ScoreAsync(reviewId);

        Review scored = await ReloadAsync(reviewId);
        Assert.NotNull(scored.ProcessedAt);
        Assert.NotNull(scored.SentimentScore);
        Assert.True(scored.SentimentScore!.Value > 0); // a clearly positive body
        Assert.Equal(SentimentLabel.Positive, scored.SentimentLabel);
    }

    [Fact]
    public async Task ScoreAsync_IsIdempotent()
    {
        Guid reviewId = await SeedReviewAsync("Good and great.");

        await ScoreAsync(reviewId);
        DateTimeOffset? firstProcessed = (await ReloadAsync(reviewId)).ProcessedAt;
        Assert.NotNull(firstProcessed);

        await ScoreAsync(reviewId); // second pass must be a no-op (already scored)
        Assert.Equal(firstProcessed, (await ReloadAsync(reviewId)).ProcessedAt);
    }

    [Fact]
    public async Task GetUnscoredIds_ContainsPending_ThenExcludesScored()
    {
        Guid reviewId = await SeedReviewAsync("Terrible, it broke immediately.");

        using IServiceScope scope = _factory.Services.CreateScope();
        IReviewSentimentService sentiment = scope.ServiceProvider.GetRequiredService<IReviewSentimentService>();

        IReadOnlyList<Guid> before = await sentiment.GetUnscoredIdsAsync(10_000, CancellationToken.None);
        Assert.Contains(reviewId, before);

        await sentiment.ScoreAsync(reviewId, CancellationToken.None);

        IReadOnlyList<Guid> after = await sentiment.GetUnscoredIdsAsync(10_000, CancellationToken.None);
        Assert.DoesNotContain(reviewId, after);
    }

    [Fact]
    public async Task ScoreAsync_AdapterFailure_LeavesReviewUnscored()
    {
        Guid reviewId = await SeedReviewAsync("Anything at all.");

        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        // A throwing adapter stands in for an Azure outage: the failure must propagate (the hosted
        // service catches it) and leave the review unscored so the slow re-scan retries it.
        var service = new ReviewSentimentService(db, new ThrowingAdapter(), TimeProvider.System);

        await Assert.ThrowsAsync<ExternalServiceException>(() => service.ScoreAsync(reviewId, CancellationToken.None));

        Review review = await ReloadAsync(reviewId);
        Assert.Null(review.ProcessedAt);
        Assert.Null(review.SentimentScore);
    }

    private sealed class ThrowingAdapter : ITextAnalyticsAdapter
    {
        public Task<SentimentResult> AnalyzeAsync(string text, CancellationToken ct) =>
            throw new ExternalServiceException("simulated AI outage");
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private async Task ScoreAsync(Guid reviewId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IReviewSentimentService sentiment = scope.ServiceProvider.GetRequiredService<IReviewSentimentService>();
        await sentiment.ScoreAsync(reviewId, CancellationToken.None);
    }

    private async Task<Review> ReloadAsync(Guid reviewId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        return await db.Reviews.AsNoTracking().FirstAsync(r => r.Id == reviewId);
    }

    private async Task<Guid> SeedReviewAsync(string body)
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();

        var user = new ApplicationUser
        {
            UserName = $"u-{suffix}@test.local",
            NormalizedUserName = $"U-{suffix}@TEST.LOCAL",
            Email = $"u-{suffix}@test.local",
            NormalizedEmail = $"U-{suffix}@TEST.LOCAL",
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        var profile = new CustomerProfile { AppUserId = user.Id, DisplayName = $"User {suffix}" };
        var category = new Category { Name = $"Cat {suffix}", Slug = $"cat-{suffix}" };
        var product = new Product
        {
            Category = category,
            Sku = $"SKU-{suffix}",
            Slug = $"product-{suffix}",
            Name = $"Product {suffix}",
            IsPublished = true,
        };
        var review = new Review { Product = product, CustomerProfile = profile, Rating = 4, Body = body };

        db.AddRange(user, profile, category, product);
        db.Reviews.Add(review);
        await db.SaveChangesAsync();
        return review.Id;
    }
}
