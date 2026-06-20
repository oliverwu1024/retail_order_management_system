namespace Retail.Api.DTOs.Responses;

/// <summary>
/// One persisted chat turn for the admin diagnostics history (Phase 5A). <c>Role</c> is the persisted
/// <c>ChatMessageRole</c> (User / Assistant / System / Tool); <c>ToolName</c> + <c>ToolPayloadJson</c>
/// are populated only for a Tool row.
/// </summary>
public sealed record ChatMessageDto(
    string Role,
    string Content,
    string? ToolName,
    string? ToolPayloadJson,
    DateTimeOffset CreatedAt);
