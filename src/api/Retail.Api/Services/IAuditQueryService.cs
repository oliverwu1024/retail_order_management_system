using Retail.Api.Common.Models;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>Read-only search over the immutable audit trail (Phase 3 §7).</summary>
public interface IAuditQueryService
{
    /// <summary>Searches audit rows by actor / entity / date range, newest first, paged.</summary>
    Task<PagedResult<AuditLogDto>> SearchAsync(AuditLogListQuery query, CancellationToken ct);
}
