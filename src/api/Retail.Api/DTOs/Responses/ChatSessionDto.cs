namespace Retail.Api.DTOs.Responses;

/// <summary>A support-chat session summary for the admin diagnostics list (Phase 5A).</summary>
public sealed record ChatSessionDto(
    Guid Id,
    string ConversationId,
    Guid? CustomerProfileId,
    DateTimeOffset StartedAt,
    DateTimeOffset LastMessageAt,
    int MessageCount);
