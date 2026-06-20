using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Repositories;

/// <summary>EF Core implementation of <see cref="IChatRepository"/> (Phase 5A).</summary>
public sealed class ChatRepository : IChatRepository
{
    private readonly RetailDbContext _db;

    public ChatRepository(RetailDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<ChatSession?> GetSessionByConversationIdAsync(string conversationId, CancellationToken ct) =>
        // Tracked (not AsNoTracking): the service bumps LastMessageAt on an existing session.
        await _db.ChatSessions.FirstOrDefaultAsync(s => s.ConversationId == conversationId, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(Guid sessionId, CancellationToken ct) =>
        await _db.ChatMessages.AsNoTracking()
            .Where(m => m.ChatSessionId == sessionId)
            // Every row inserted in one turn shares an identical CreatedAt (the AuditingInterceptor
            // stamps the whole batch with one timestamp), so CreatedAt alone is an ambiguous order.
            // ThenBy(Role) breaks the tie deterministically: within a turn the only replayed pair is
            // one User (=1) and one Assistant (=2), so User always precedes Assistant — which the
            // Anthropic API requires (a transcript must not start with / mis-order assistant turns).
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Role)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<ChatSession> CreateSessionResolvingRaceAsync(ChatSession session, CancellationToken ct)
    {
        _db.ChatSessions.Add(session);
        try
        {
            await _db.SaveChangesAsync(ct);
            return session;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Lost the unique-ConversationId race: a concurrent first turn created this conversation
            // first. Drop our duplicate and adopt the winner instead of surfacing a 500.
            _db.Entry(session).State = EntityState.Detached;
            return await _db.ChatSessions.FirstAsync(s => s.ConversationId == session.ConversationId, ct);
        }
    }

    /// <inheritdoc />
    public void AddMessage(ChatMessage message) => _db.ChatMessages.Add(message);

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken ct) => await _db.SaveChangesAsync(ct);
}
