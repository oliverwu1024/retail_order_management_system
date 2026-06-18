using System.Text.Json;
using Retail.Api.Ai.Contracts;
using Retail.Api.Ai.Providers;
using Xunit;

namespace Retail.Tests.Unit.Ai;

/// <summary>Unit tests for the hermetic LLM stub (Phase 4 Chunk 2 / 4).</summary>
public class StubLlmClientTests
{
    [Fact]
    public async Task CompleteAsync_ReturnsForcedTool_WithCopyShape()
    {
        var stub = new StubLlmClient();
        var request = new LlmRequest(
            Model: "copy",
            SystemPrompt: "sys",
            Messages: [new LlmMessage(LlmRole.User, Text: "Write copy.")],
            Tools: [new LlmTool("emit_product_copy", "desc", default)],
            ToolChoice: LlmToolChoice.RequiredTool("emit_product_copy"));

        LlmCompletion completion = await stub.CompleteAsync(request, CancellationToken.None);

        LlmToolUse tool = Assert.Single(completion.ToolUses);
        Assert.Equal("emit_product_copy", tool.Name);

        JsonElement input = tool.Input;
        Assert.False(string.IsNullOrWhiteSpace(input.GetProperty("description").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(input.GetProperty("seoTitle").GetString()));
        Assert.True(input.GetProperty("bulletPoints").GetArrayLength() > 0);
    }
}
