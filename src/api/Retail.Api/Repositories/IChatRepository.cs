using Retail.Api.Domain.Entities;

namespace Retail.Api.Repositories;

/// <summary>Persistence for support-chat sessions and their messages (Phase 5A). Append-only.</summary>
public interface IChatRepository
{
    /// <summary>
    /// The session for a client conversation id, TRACKED (the caller bumps <c>LastMessageAt</c>), or
    /// null if this is the first turn. The unique <c>ConversationId</c> index means at most one match.
    /// </summary>
    Task<ChatSession?> GetSessionByConversationIdAsync(string conversationId, CancellationToken ct);

    /// <summary>A session's messages oldest-first, read-only — the prior turns replayed to the model.</summary>
    Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(Guid sessionId, CancellationToken ct);

    /// <summary>
    /// Inserts a new session, resolving a concurrent-insert race on the unique <c>ConversationId</c>:
    /// if a competing first turn won, returns THAT (already-persisted, tracked) session instead of
    /// throwing. The returned session is always persisted.
    /// </summary>
    Task<ChatSession> CreateSessionResolvingRaceAsync(ChatSession session, CancellationToken ct);

    /// <summary>Stages a new message for insert.</summary>
    void AddMessage(ChatMessage message);

    Task SaveChangesAsync(CancellationToken ct);
}
