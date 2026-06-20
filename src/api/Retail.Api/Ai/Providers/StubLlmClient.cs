using System.Text.Json;
using Retail.Api.Ai.Contracts;

namespace Retail.Api.Ai.Providers;

/// <summary>
/// Hermetic <see cref="ILlmClient"/> for <c>Ai:Mode=stub</c> (the default). Returns deterministic
/// completions so CopyGen, the chat loop, the integration tests, CI, and the demo all run with no
/// API key and no network.
/// </summary>
/// <remarks>
/// Two shapes, discriminated by the request:
/// <list type="bullet">
/// <item><b>CopyGen</b> forces a tool (<see cref="LlmToolChoice.RequiredTool"/>) → a single canned
/// <c>emit_product_copy</c> tool-use.</item>
/// <item><b>Chat</b> uses auto tool-choice (<see cref="LlmToolChoice.Auto"/>, i.e.
/// <c>RequiredToolName is null</c>) with tools present → a deterministic two-step transcript: the
/// first call emits a <c>list_my_recent_orders</c> tool-use; once a tool result is in the transcript
/// it returns a final text turn (<c>end_turn</c>). This exercises the real multi-turn loop
/// hermetically. The chat branch is checked FIRST, because the forced-tool path always returns
/// <c>tool_use</c> — a chat request would otherwise mis-fire and never reach <c>end_turn</c>.</item>
/// </list>
/// </remarks>
public sealed class StubLlmClient : ILlmClient
{
    public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        bool isChat = request.ToolChoice?.RequiredToolName is null && request.Tools is { Count: > 0 };
        return Task.FromResult(isChat ? BuildChatCompletion(request) : BuildForcedToolCompletion(request));
    }

    private static LlmCompletion BuildChatCompletion(LlmRequest request)
    {
        // Second pass: a tool result is already in the transcript → wrap up with a text turn.
        bool hasToolResult = request.Messages.Any(m => m.ToolResults is { Count: > 0 });
        if (hasToolResult)
        {
            return new LlmCompletion(
                Text: "Here's what I found on your recent orders. Let me know if you'd like details on any of them.",
                ToolUses: Array.Empty<LlmToolUse>(),
                Usage: new LlmUsage(InputTokens: 0, OutputTokens: 0),
                StopReason: "end_turn");
        }

        // First pass: call a no-argument read tool to drive the loop deterministically.
        var toolUse = new LlmToolUse(Id: "stub-chat-tooluse-1", Name: "list_my_recent_orders", Input: EmptyArgs);
        return new LlmCompletion(
            Text: null,
            ToolUses: new[] { toolUse },
            Usage: new LlmUsage(InputTokens: 0, OutputTokens: 0),
            StopReason: "tool_use");
    }

    private static LlmCompletion BuildForcedToolCompletion(LlmRequest request)
    {
        string toolName = request.ToolChoice?.RequiredToolName
            ?? request.Tools?.FirstOrDefault()?.Name
            ?? "emit_product_copy";

        var toolUse = new LlmToolUse(Id: "stub-tooluse-1", Name: toolName, Input: CannedCopy);
        return new LlmCompletion(
            Text: null,
            ToolUses: new[] { toolUse },
            Usage: new LlmUsage(InputTokens: 0, OutputTokens: 0),
            StopReason: "tool_use");
    }

    private static readonly JsonElement EmptyArgs = JsonSerializer.SerializeToElement(new { });

    // A plausible, deterministic emit_product_copy payload (camelCase, matching the tool schema).
    private static readonly JsonElement CannedCopy = JsonSerializer.SerializeToElement(new
    {
        description = "Crafted from premium materials with a clean, considered design, this piece balances "
            + "everyday durability with understated style — a dependable choice that looks as good as it performs.",
        seoTitle = "Premium Everyday Essential — Thoughtfully Designed",
        seoMetaDescription = "A premium everyday essential built from quality materials: durable, versatile, "
            + "and designed to last. (Sample copy — generated in stub mode.)",
        bulletPoints = new[]
        {
            "Premium materials built to last",
            "Clean, versatile design for everyday use",
            "Thoughtfully finished details",
        },
    });
}
