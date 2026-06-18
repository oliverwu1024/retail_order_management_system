using System.Text.Json;
using Retail.Api.Ai.Contracts;

namespace Retail.Api.Ai.Providers;

/// <summary>
/// Hermetic <see cref="ILlmClient"/> for <c>Ai:Mode=stub</c> (the default). Returns a deterministic
/// tool-use so CopyGen, the integration tests, CI, and the demo all run with no API key and no
/// network. Provider-agnostic: it honors whichever tool the caller forced via
/// <see cref="LlmToolChoice.RequiredTool"/>, with a canned payload matching the copy-gen schema.
/// </summary>
public sealed class StubLlmClient : ILlmClient
{
    public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        string toolName = request.ToolChoice?.RequiredToolName
            ?? request.Tools?.FirstOrDefault()?.Name
            ?? "emit_product_copy";

        var toolUse = new LlmToolUse(Id: "stub-tooluse-1", Name: toolName, Input: CannedCopy);

        var completion = new LlmCompletion(
            Text: null,
            ToolUses: new[] { toolUse },
            Usage: new LlmUsage(InputTokens: 0, OutputTokens: 0),
            StopReason: "tool_use");

        return Task.FromResult(completion);
    }

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
