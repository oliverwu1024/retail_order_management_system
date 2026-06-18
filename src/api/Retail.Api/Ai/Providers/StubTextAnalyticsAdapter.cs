using Retail.Api.Common.Enums;

namespace Retail.Api.Ai.Providers;

/// <summary>
/// Hermetic <see cref="ITextAnalyticsAdapter"/> for <c>Ai:Mode=stub</c> (the default). A small,
/// deterministic keyword scorer so the sentiment pipeline, the integration tests, CI, and the demo
/// all run with no Azure resource and no network — and produce varied scores (positive / negative /
/// mixed) so the admin dashboard and "Products Needing Attention" panel show real data.
/// </summary>
public sealed class StubTextAnalyticsAdapter : ITextAnalyticsAdapter
{
    private static readonly string[] PositiveWords =
        ["good", "great", "love", "excellent", "perfect", "amazing", "fantastic", "happy", "recommend", "best"];

    private static readonly string[] NegativeWords =
        ["bad", "terrible", "awful", "hate", "broke", "broken", "disappointed", "poor", "worst", "cheap"];

    public Task<SentimentResult> AnalyzeAsync(string text, CancellationToken ct)
    {
        string lower = (text ?? string.Empty).ToLowerInvariant();
        int positive = PositiveWords.Count(lower.Contains);
        int negative = NegativeWords.Count(lower.Contains);
        int total = positive + negative;

        decimal score = total == 0 ? 0m : Math.Round((decimal)(positive - negative) / total, 3);
        SentimentLabel label = (positive, negative) switch
        {
            (0, 0) => SentimentLabel.Neutral,
            ( > 0, > 0) => SentimentLabel.Mixed,
            _ when score > 0 => SentimentLabel.Positive,
            _ when score < 0 => SentimentLabel.Negative,
            _ => SentimentLabel.Neutral,
        };

        return Task.FromResult(new SentimentResult(score, label));
    }
}
