using Retail.Api.Ai.Contracts;

namespace Retail.Api.Services;

/// <summary>
/// Executes a chat tool the model invoked, scoped to the authenticated customer, and returns the
/// tool result as a JSON string to feed back to the model (Phase 5A).
/// </summary>
public interface IChatToolExecutor
{
    /// <summary>
    /// Runs <paramref name="toolUse"/> on behalf of <paramref name="appUserId"/> (the authenticated
    /// Identity user — never an id supplied by the model). Returns a JSON string for the
    /// <c>tool_result</c>. Never throws for ordinary "not found" / unknown-tool cases — it returns a
    /// structured result the model can relay.
    /// </summary>
    Task<string> ExecuteAsync(string appUserId, LlmToolUse toolUse, CancellationToken ct);
}
