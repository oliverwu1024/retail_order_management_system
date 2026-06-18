namespace Retail.Api.Ai;

/// <summary>
/// The sentiment-analysis seam (CODING_STANDARDS §97 / REQUIREMENTS Task 4.3.1). The concrete
/// provider (Azure AI Language live, or the hermetic stub) is chosen by <c>Ai:Mode</c> at DI time,
/// so the sentiment scorer never references a provider type.
/// </summary>
public interface ITextAnalyticsAdapter
{
    /// <summary>Scores the sentiment of a single piece of text. Throws <c>ExternalServiceException</c> if the live service fails.</summary>
    Task<SentimentResult> AnalyzeAsync(string text, CancellationToken ct);
}
