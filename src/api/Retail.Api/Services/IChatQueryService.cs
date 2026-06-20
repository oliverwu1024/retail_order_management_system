using Retail.Api.Common.Models;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>Read-side for admin support-chat diagnostics (Phase 5A) — gated by <c>Chat.View</c>.</summary>
public interface IChatQueryService
{
    /// <summary>Chat sessions, most-recently-active first, paged.</summary>
    Task<PagedResult<ChatSessionDto>> ListSessionsAsync(ChatSessionListQuery query, CancellationToken ct);

    /// <summary>One session with its full message history. Throws <c>NotFoundException</c> if missing.</summary>
    Task<ChatSessionDetailDto> GetSessionAsync(Guid id, CancellationToken ct);
}
