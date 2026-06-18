using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Retail.Api.Common.Enums;
using Retail.Api.Exceptions;

namespace Retail.Api.Ai.Providers;

/// <summary>
/// Live <see cref="ITextAnalyticsAdapter"/> — a typed <see cref="HttpClient"/> on the Azure AI
/// Language REST API (<c>POST {endpoint}/language/:analyze-text</c>, <c>kind=SentimentAnalysis</c>).
/// Registered with <c>AddStandardResilienceHandler()</c> (Polly). Maps the document-level result to
/// our <see cref="SentimentResult"/>: <c>Score = positive − negative</c> confidence, in −1..1.
/// </summary>
/// <remarks>
/// Same reconciliation as <c>AnthropicLlmClient</c>: we call the documented REST contract via a typed
/// HttpClient rather than the <c>Azure.AI.TextAnalytics</c> SDK — a stable wire contract, no
/// SDK-version coupling, consistent with the LLM provider (Chunk 2).
/// </remarks>
public sealed class AzureTextAnalyticsAdapter : ITextAnalyticsAdapter
{
    private const string ApiVersion = "2023-04-01";

    private readonly HttpClient _http;
    private readonly AzureAiLanguageOptions _options;

    public AzureTextAnalyticsAdapter(HttpClient http, IOptions<AzureAiLanguageOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<SentimentResult> AnalyzeAsync(string text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint) || string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new ExternalServiceException("Azure AI Language is not configured.");
        }

        var body = new
        {
            kind = "SentimentAnalysis",
            analysisInput = new { documents = new[] { new { id = "1", language = "en", text } } },
        };

        string url = $"{_options.Endpoint.TrimEnd('/')}/language/:analyze-text?api-version={ApiVersion}";
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Ocp-Apim-Subscription-Key", _options.ApiKey);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ExternalServiceException("Azure AI Language is currently unavailable.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ExternalServiceException($"Azure AI Language returned an error ({(int)response.StatusCode}).");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return Map(doc.RootElement);
    }

    private static SentimentResult Map(JsonElement root)
    {
        JsonElement document = root.GetProperty("results").GetProperty("documents")[0];
        string sentiment = document.GetProperty("sentiment").GetString() ?? "neutral";

        JsonElement scores = document.GetProperty("confidenceScores");
        decimal positive = scores.GetProperty("positive").GetDecimal();
        decimal negative = scores.GetProperty("negative").GetDecimal();
        decimal score = Math.Round(positive - negative, 3);

        SentimentLabel label = sentiment switch
        {
            "positive" => SentimentLabel.Positive,
            "negative" => SentimentLabel.Negative,
            "mixed" => SentimentLabel.Mixed,
            _ => SentimentLabel.Neutral,
        };

        return new SentimentResult(score, label);
    }
}
