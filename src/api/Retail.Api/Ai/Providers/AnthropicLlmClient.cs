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

        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            return MapCompletion(doc.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            // A malformed / unexpected provider response is an EXTERNAL failure, not a server bug — so
            // surface it as ExternalServiceException. That keeps the failure path uniform: CopyGen maps
            // it to a 503, and the chat loop catches it to degrade to a friendly HTTP 200 (scope §3.5/§6).
            // (OperationCanceledException is intentionally NOT caught — cancellation must propagate.)
            throw new ExternalServiceException("The AI provider returned a response that could not be parsed.", ex);
        }
    }

    private Dictionary<string, object?> BuildRequestBody(LlmRequest request)
    {
        // Each message maps to a content-block ARRAY when it carries tool_use / tool_result blocks
        // (the multi-turn chat loop), or a plain string for an ordinary text turn. We never emit
        // content:"" on a tool-bearing turn — the Messages API rejects empty-string content.
        var messages = request.Messages.Select(m => new
        {
            role = m.Role == LlmRole.Assistant ? "assistant" : "user",
            content = BuildMessageContent(m),
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

    /// <summary>
    /// Maps a provider-agnostic message to the Anthropic wire <c>content</c>: a plain string for a
    /// text-only turn, or a content-block array for an assistant turn that called tools
    /// (<c>tool_use</c> blocks) / a user turn carrying results (<c>tool_result</c> blocks).
    /// </summary>
    private static object BuildMessageContent(LlmMessage m)
    {
        // tool_result blocks ride a USER message (Anthropic has no "tool" role).
        if (m.ToolResults is { Count: > 0 })
        {
            return m.ToolResults
                .Select(r => new { type = "tool_result", tool_use_id = r.ToolUseId, content = r.Content })
                .ToArray();
        }

        // An assistant turn that called tools: an optional leading text block, then one tool_use block
        // per call. Every tool_use id must be answered by a matching tool_result in the next turn.
        if (m.ToolUses is { Count: > 0 })
        {
            var blocks = new List<object>();
            if (!string.IsNullOrEmpty(m.Text))
            {
                blocks.Add(new { type = "text", text = m.Text });
            }
            blocks.AddRange(m.ToolUses.Select(u =>
                new { type = "tool_use", id = u.Id, name = u.Name, input = (object)u.Input }));
            return blocks;
        }

        // Ordinary text turn → a plain string (Anthropic accepts a string for content).
        return m.Text ?? string.Empty;
    }

    private string ResolveModel(string logical) => logical switch
    {
        "copy" => _settings.Models.Copy,
        "chat" => _settings.Models.Chat,
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
                        // Concatenate in case the assistant interleaves multiple text blocks with tool_use.
                        text = (text ?? string.Empty) + block.GetProperty("text").GetString();
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
