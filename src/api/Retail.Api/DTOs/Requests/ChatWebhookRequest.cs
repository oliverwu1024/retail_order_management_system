namespace Retail.Api.DTOs.Requests;

/// <summary>One customer chat turn posted to <c>/api/v1/chat/webhook</c> (Phase 5A).</summary>
public sealed record ChatWebhookRequest
{
    /// <summary>The client-generated conversation id (a GUID) — the session this turn belongs to.</summary>
    public string ConversationId { get; init; } = string.Empty;

    /// <summary>The customer's message.</summary>
    public string Message { get; init; } = string.Empty;
}
