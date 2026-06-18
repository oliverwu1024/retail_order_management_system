using Retail.Api.Ai;
using Retail.Api.Ai.Providers;
using Retail.Api.Common.Enums;
using Xunit;

namespace Retail.Tests.Unit.Ai;

/// <summary>Unit tests for the hermetic keyword sentiment scorer (Phase 4 Chunk 3 / 4).</summary>
public class StubTextAnalyticsAdapterTests
{
    private readonly StubTextAnalyticsAdapter _adapter = new();

    [Theory]
    [InlineData("This is great, I love it. Excellent quality.", SentimentLabel.Positive)]
    [InlineData("Terrible and it broke. Worst purchase ever.", SentimentLabel.Negative)]
    [InlineData("It's good, but it broke after a week.", SentimentLabel.Mixed)]
    [InlineData("It is a product that exists.", SentimentLabel.Neutral)]
    public async Task AnalyzeAsync_AssignsExpectedLabel(string text, SentimentLabel expected)
    {
        SentimentResult result = await _adapter.AnalyzeAsync(text, CancellationToken.None);
        Assert.Equal(expected, result.Label);
    }

    [Fact]
    public async Task AnalyzeAsync_PositiveText_ScoresPositive_InRange()
    {
        SentimentResult result = await _adapter.AnalyzeAsync("love it — great, excellent", CancellationToken.None);
        Assert.True(result.Score > 0);
        Assert.InRange(result.Score, -1m, 1m);
    }

    [Fact]
    public async Task AnalyzeAsync_NegativeText_ScoresNegative()
    {
        SentimentResult result = await _adapter.AnalyzeAsync("terrible, awful, hate it", CancellationToken.None);
        Assert.True(result.Score < 0);
    }

    [Fact]
    public async Task AnalyzeAsync_NeutralText_ScoresZero()
    {
        SentimentResult result = await _adapter.AnalyzeAsync("a plain factual sentence", CancellationToken.None);
        Assert.Equal(0m, result.Score);
    }
}
