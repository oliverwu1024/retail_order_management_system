using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>The support-chat orchestrator (Phase 5A): one customer turn in, one assistant turn out.</summary>
public interface IChatService
{
    /// <summary>
    /// Handles one chat turn for the authenticated customer (<paramref name="appUserId"/>): upserts
    /// the session, persists the turn, runs the Claude tool-use loop, and returns the assistant's
    /// reply. On an AI-provider outage it returns a friendly message (HTTP 200), never a 5xx.
    /// </summary>
    Task<ChatTurnDto> HandleTurnAsync(string appUserId, ChatWebhookRequest request, CancellationToken ct);
}
