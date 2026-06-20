using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Retail.Api.Ai;
using Retail.Api.Ai.Contracts;
using Retail.Api.Ai.Providers;

namespace Retail.Tests.Unit.Ai;

/// <summary>
/// Unit tests for the load-bearing Phase-5A seam edit: <see cref="AnthropicLlmClient"/> serialising a
/// multi-turn transcript to the Anthropic wire shape (tool_use / tool_result content blocks) and
/// parsing a tool-use response. Driven by a capturing <see cref="HttpMessageHandler"/> — no network.
/// </summary>
public class AnthropicLlmClientTests
{
    private const string ToolUseResponse = """
        {
          "content": [
            { "type": "text", "text": "Let me check that for you." },
            { "type": "tool_use", "id": "tu_1", "name": "get_order", "input": { "orderNumber": 10012 } }
          ],
          "usage": { "input_tokens": 11, "output_tokens": 9 },
          "stop_reason": "tool_use"
        }
        """;

    [Fact]
    public async Task CompleteAsync_SerialisesToolBlocks_AndResolvesChatModel()
    {
        var handler = new CapturingHandler(ToolUseResponse);
        AnthropicLlmClient client = BuildClient(handler);

        var request = new LlmRequest(
            Model: "chat",
            SystemPrompt: "You are a helper.",
            Messages: new[]
            {
                new LlmMessage(LlmRole.User, Text: "Where is order 10012?"),
                new LlmMessage(LlmRole.Assistant, Text: "Checking.",
                    ToolUses: new[] { new LlmToolUse("tu_1", "get_order", JsonSerializer.SerializeToElement(new { orderNumber = 10012 })) }),
                new LlmMessage(LlmRole.User,
                    ToolResults: new[] { new LlmToolResult("tu_1", "{\"found\":true}") }),
            },
            Tools: new[] { new LlmTool("get_order", "Get an order.", JsonSerializer.SerializeToElement(new { type = "object" })) },
            ToolChoice: LlmToolChoice.Auto);

        await client.CompleteAsync(request, CancellationToken.None);

        Assert.NotNull(handler.CapturedBody);
        using JsonDocument body = JsonDocument.Parse(handler.CapturedBody!);
        JsonElement root = body.RootElement;

        // Logical "chat" resolved to the configured model id.
        Assert.Equal("claude-test-chat", root.GetProperty("model").GetString());

        JsonElement messages = root.GetProperty("messages");

        // A plain-text turn (messages[0]) serialises to a JSON STRING content — not an array; the API
        // rejects the wrong shape, and this is the default BuildMessageContent branch.
        Assert.Equal(JsonValueKind.String, messages[0].GetProperty("content").ValueKind);

        // Assistant turn → content array; the text block comes FIRST, then tool_use (Anthropic order).
        JsonElement assistantContent = messages[1].GetProperty("content");
        Assert.Equal(JsonValueKind.Array, assistantContent.ValueKind);
        Assert.Equal("text", assistantContent[0].GetProperty("type").GetString());
        Assert.Equal("tool_use", assistantContent[1].GetProperty("type").GetString());
        Assert.Equal("tu_1", assistantContent[1].GetProperty("id").GetString());
        Assert.Equal("get_order", assistantContent[1].GetProperty("name").GetString());

        // User tool-result turn → tool_result block keyed by tool_use_id.
        JsonElement resultContent = messages[2].GetProperty("content");
        JsonElement resultBlock = resultContent.EnumerateArray().Single();
        Assert.Equal("tool_result", resultBlock.GetProperty("type").GetString());
        Assert.Equal("tu_1", resultBlock.GetProperty("tool_use_id").GetString());
    }

    [Fact]
    public async Task CompleteAsync_ParsesToolUseResponse()
    {
        AnthropicLlmClient client = BuildClient(new CapturingHandler(ToolUseResponse));

        LlmCompletion completion = await client.CompleteAsync(
            new LlmRequest("chat", "system", new[] { new LlmMessage(LlmRole.User, Text: "hi") }),
            CancellationToken.None);

        Assert.Equal("tool_use", completion.StopReason);
        Assert.Equal("Let me check that for you.", completion.Text);
        LlmToolUse use = Assert.Single(completion.ToolUses);
        Assert.Equal("get_order", use.Name);
        Assert.Equal(10012, use.Input.GetProperty("orderNumber").GetInt32());
        Assert.Equal(11, completion.Usage.InputTokens);
    }

    private static AnthropicLlmClient BuildClient(HttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var settings = Options.Create(new AiSettings
        {
            Mode = "live",
            ApiKey = "test-key",
            Models = new AiModelMap { Chat = "claude-test-chat" },
        });
        return new AnthropicLlmClient(http, settings, NullLogger<AnthropicLlmClient>.Instance);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly string _responseJson;
        public string? CapturedBody { get; private set; }

        public CapturingHandler(string responseJson) => _responseJson = responseJson;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                CapturedBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseJson),
            };
        }
    }
}
