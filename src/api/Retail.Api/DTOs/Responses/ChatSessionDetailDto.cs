namespace Retail.Api.DTOs.Responses;

/// <summary>A support-chat session with its full message history (admin diagnostics, Phase 5A).</summary>
public sealed record ChatSessionDetailDto(
    Guid Id,
    string ConversationId,
    Guid? CustomerProfileId,
    DateTimeOffset StartedAt,
    DateTimeOffset LastMessageAt,
    IReadOnlyList<ChatMessageDto> Messages);
