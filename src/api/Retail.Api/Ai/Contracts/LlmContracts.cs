using System.Text.Json;

namespace Retail.Api.Ai.Contracts;

// ─────────────────────────────────────────────────────────────────────────────
//  Provider-agnostic LLM contracts (ADR-0005, CODING_STANDARDS § AI Client 抽象).
//
//  These are OUR records — never an SDK's types. Services (CopyGenService, and the
//  Phase-5 ChatService) depend only on these + ILlmClient, so the concrete provider
//  (AnthropicLlmClient / StubLlmClient) can change with zero service-layer churn.
//  The shape is the cross-provider lowest common denominator: messages + tools +
//  tool_choice + usage.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Author of a message in an LLM exchange.</summary>
public enum LlmRole
{
    User,
    Assistant,
}

/// <summary>One message in the conversation. Carries text and/or tool blocks (tool blocks used by the Phase-5 chat loop).</summary>
public record LlmMessage(
    LlmRole Role,
    string? Text = null,
    IReadOnlyList<LlmToolUse>? ToolUses = null,
    IReadOnlyList<LlmToolResult>? ToolResults = null);

/// <summary>A tool the model may call. <paramref name="InputSchema"/> is a JSON Schema describing the tool's arguments.</summary>
public record LlmTool(string Name, string Description, JsonElement InputSchema);

/// <summary>A tool invocation the model emitted. <paramref name="Input"/> is the arguments JSON (validated against the tool schema).</summary>
public record LlmToolUse(string Id, string Name, JsonElement Input);

/// <summary>The result of executing a tool, fed back to the model (Phase-5 chat loop).</summary>
public record LlmToolResult(string ToolUseId, string Content);

/// <summary>
/// How the model should choose tools. <see cref="Auto"/> lets it decide; <see cref="RequiredTool"/>
/// forces a specific tool — the mechanism CopyGen uses to guarantee structured JSON output.
/// </summary>
public record LlmToolChoice(string Kind, string? RequiredToolName = null)
{
    /// <summary>Model decides whether/which tool to call.</summary>
    public static LlmToolChoice Auto => new("auto");

    /// <summary>Model must call exactly the named tool (structured-output guarantee).</summary>
    public static LlmToolChoice RequiredTool(string name) => new("required", name);
}

/// <summary>A completion request: a logical model name, system prompt, messages, and optional tools/tool-choice.</summary>
public record LlmRequest(
    string Model,
    string SystemPrompt,
    IReadOnlyList<LlmMessage> Messages,
    IReadOnlyList<LlmTool>? Tools = null,
    LlmToolChoice? ToolChoice = null,
    int? MaxTokens = null,
    double? Temperature = null,
    bool EnableCaching = false);

/// <summary>Token accounting returned with a completion (drives cost/usage logging).</summary>
public record LlmUsage(
    int InputTokens,
    int OutputTokens,
    int? CacheCreationTokens = null,
    int? CacheReadTokens = null);

/// <summary>A completion: free text and/or tool invocations, plus usage and the model's stop reason.</summary>
public record LlmCompletion(
    string? Text,
    IReadOnlyList<LlmToolUse> ToolUses,
    LlmUsage Usage,
    string StopReason);
