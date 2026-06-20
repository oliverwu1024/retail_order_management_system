namespace Retail.Api.DTOs.Responses;

/// <summary>
/// The assistant's reply for one chat turn (Phase 5A). <see cref="ProposedAction"/> is set (Chunk 3)
/// when the model proposed a confirmation-gated action (e.g. a refund) — the storefront renders a
/// confirmation card and only an explicit user click performs it.
/// </summary>
public sealed record ChatTurnDto(string Reply, ChatProposedAction? ProposedAction = null);
