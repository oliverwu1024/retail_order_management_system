using Microsoft.EntityFrameworkCore;
using Retail.Api.Common.Enums;
using Retail.Api.Common.Models;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Exceptions;

namespace Retail.Api.Services;

/// <summary>
/// Read-only admin diagnostics over chat sessions/messages (Phase 5A). DbContext-direct — a technical
/// read model (like the audit log), no domain rules; gated by <c>Chat.View</c> at the controller.
/// </summary>
public sealed class ChatQueryService : IChatQueryService
{
    private readonly RetailDbContext _db;

    public ChatQueryService(RetailDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<PagedResult<ChatSessionDto>> ListSessionsAsync(ChatSessionListQuery query, CancellationToken ct)
    {
        int safePage = query.Page < 1 ? 1 : query.Page;
        int safeSize = Math.Clamp(query.PageSize, 1, 100);

        IQueryable<ChatSession> sessions = _db.ChatSessions.AsNoTracking();
        int total = await sessions.CountAsync(ct);

        List<ChatSessionDto> items = await sessions
            .OrderByDescending(s => s.LastMessageAt)
            .Skip((safePage - 1) * safeSize)
            .Take(safeSize)
            // s.Messages.Count translates to a correlated COUNT subquery.
            .Select(s => new ChatSessionDto(
                s.Id, s.ConversationId, s.CustomerProfileId, s.StartedAt, s.LastMessageAt, s.Messages.Count))
            .ToListAsync(ct);

        return new PagedResult<ChatSessionDto>(items, total, safePage, safeSize);
    }

    /// <inheritdoc />
    public async Task<ChatSessionDetailDto> GetSessionAsync(Guid id, CancellationToken ct)
    {
        ChatSession? session = await _db.ChatSessions.AsNoTracking()
            .Include(s => s.Messages)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (session is null)
        {
            throw new NotFoundException($"Chat session '{id}' was not found.");
        }

        // Every row in a turn shares one CreatedAt (the interceptor stamps the batch), so order
        // User → Tool → Assistant chronologically — the raw enum value would put Assistant (2) before
        // Tool (4). Enum→string mapping also runs in memory (it doesn't translate to SQL).
        var messages = session.Messages
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => DisplayRank(m.Role))
            .Select(m => new ChatMessageDto(m.Role.ToString(), m.Content, m.ToolName, m.ToolPayloadJson, m.CreatedAt))
            .ToList();

        return new ChatSessionDetailDto(
            session.Id, session.ConversationId, session.CustomerProfileId,
            session.StartedAt, session.LastMessageAt, messages);
    }

    /// <summary>Chronological tiebreak within a single turn: a tool call precedes the assistant reply it produced.</summary>
    private static int DisplayRank(ChatMessageRole role) => role switch
    {
        ChatMessageRole.User => 0,
        ChatMessageRole.System => 1,
        ChatMessageRole.Tool => 2,
        ChatMessageRole.Assistant => 3,
        _ => 4,
    };
}
