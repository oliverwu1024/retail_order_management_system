using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>
/// The outcome of executing one chat tool (Phase 5A): the JSON <see cref="Content"/> fed back to the
/// model as the <c>tool_result</c>, plus an optional <see cref="ProposedAction"/> a state-changing
/// tool (e.g. <c>start_return</c>) surfaces so <c>ChatService</c> can attach it to the turn for the
/// storefront's confirmation card.
/// </summary>
public sealed record ChatToolResult(string Content, ChatProposedAction? ProposedAction = null);
