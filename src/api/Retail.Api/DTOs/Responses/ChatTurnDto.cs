namespace Retail.Api.DTOs.Responses;

/// <summary>
/// The assistant's reply for one chat turn (Phase 5A). In Chunk 3 this gains an optional
/// <c>ProposedAction</c> for the confirmation-gated <c>start_return</c> flow.
/// </summary>
public sealed record ChatTurnDto(string Reply);
