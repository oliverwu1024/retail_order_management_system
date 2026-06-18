using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Retail.Api.Ai.Contracts;
using Retail.Api.Exceptions;

namespace Retail.Api.Ai.Providers;

/// <summary>
/// Live <see cref="ILlmClient"/> — a typed <see cref="HttpClient"/> over the Anthropic Messages API
/// (<c>POST /v1/messages</c>). Registered via <c>AddHttpClient&lt;AnthropicLlmClient&gt;()
/// .AddStandardResilienceHandler()</c> so Polly (retry + circuit breaker + timeout) wraps every
/// call (CODING_STANDARDS § Resilience).
/// </summary>
/// <remarks>
/// We map our provider-agnostic contracts to/from the wire JSON here; the business layer only ever
/// sees <see cref="ILlmClient"/>. <b>Reconciliation:</b> ADR-0005 named the community
/// <c>Anthropic.SDK</c> NuGet; we implement the provider as a typed <see cref="HttpClient"/> on the
/// documented Messages API instead — a stable wire contract (<c>anthropic-version: 2023-06-01</c>),
/// no SDK-version coupling, and exactly the <c>IHttpClientFactory</c> + resilience wiring the
/// standards describe (so the ADR's <c>using Anthropic.SDK</c> compile-guard is moot).
/// </remarks>
public sealed class AnthropicLlmClient : ILlmClient
{
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _http;
    private readonly AiSettings _settings;
    private readonly ILogger<AnthropicLlmClient> _logger;

    public AnthropicLlmClient(HttpClient http, IOptions<AiSettings> settings, ILogger<AnthropicLlmClient> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = JsonContent.Create(BuildRequestBody(request)),
        };
        httpRequest.Headers.Add("x-api-key", _settings.ApiKey);
        httpRequest.Headers.Add("anthropic-version", AnthropicVersion);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, ct);
        }
        catch (HttpRequestException ex)
        {
            // Transport failure after Polly exhausted its retries.
            throw new ExternalServiceException("The AI provider is currently unavailable.", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            string detail = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Anthropic Messages API returned {Status}: {Detail}", (int)response.StatusCode, detail);
            throw new ExternalServiceException($"The AI provider returned an error ({(int)response.StatusCode}).");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return MapCompletion(doc.RootElement);
    }

    private Dictionary<string, object?> BuildRequestBody(LlmRequest request)
    {
        // Copy-gen is single-turn (one user text message); the Phase-5 chat loop will extend this
        // to map tool_use / tool_result content blocks.
        var messages = request.Messages.Select(m => new
        {
            role = m.Role == LlmRole.Assistant ? "assistant" : "user",
            content = m.Text ?? string.Empty,
        }).ToArray();

        var body = new Dictionary<string, object?>
        {
            ["model"] = ResolveModel(request.Model),
            ["max_tokens"] = request.MaxTokens ?? 1024,
            ["system"] = request.SystemPrompt,
            ["messages"] = messages,
        };

        if (request.Tools is { Count: > 0 })
        {
            body["tools"] = request.Tools.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                input_schema = t.InputSchema, // JsonElement serializes as its raw schema
            }).ToArray();
        }

        if (request.ToolChoice is { } choice)
        {
            body["tool_choice"] = choice.RequiredToolName is { } name
                ? new { type = "tool", name }
                : (object)new { type = "auto" };
        }

        return body;
    }

    private string ResolveModel(string logical) => logical switch
    {
        "copy" => _settings.Models.Copy,
        _ => logical, // already a concrete id
    };

    private static LlmCompletion MapCompletion(JsonElement root)
    {
        var toolUses = new List<LlmToolUse>();
        string? text = null;

        if (root.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement block in content.EnumerateArray())
            {
                switch (block.GetProperty("type").GetString())
                {
                    case "tool_use":
                        toolUses.Add(new LlmToolUse(
                            Id: block.GetProperty("id").GetString() ?? string.Empty,
                            Name: block.GetProperty("name").GetString() ?? string.Empty,
                            // Clone so the value survives disposal of the JsonDocument.
                            Input: block.GetProperty("input").Clone()));
                        break;
                    case "text":
                        text = block.GetProperty("text").GetString();
                        break;
                }
            }
        }

        LlmUsage usage = root.TryGetProperty("usage", out JsonElement u)
            ? new LlmUsage(
                InputTokens: u.TryGetProperty("input_tokens", out JsonElement it) ? it.GetInt32() : 0,
                OutputTokens: u.TryGetProperty("output_tokens", out JsonElement ot) ? ot.GetInt32() : 0)
            : new LlmUsage(0, 0);

        string stopReason = root.TryGetProperty("stop_reason", out JsonElement sr) ? sr.GetString() ?? string.Empty : string.Empty;

        return new LlmCompletion(text, toolUses, usage, stopReason);
    }
}
