using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail.Api.Common.Constants;
using Retail.Api.Common.Models;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Services;

namespace Retail.Api.Controllers;

/// <summary>
/// The audit-log viewer (Phase 3 §7) — search the immutable audit trail by actor / entity / date
/// range. Requires the <c>Audit.View</c> policy (Staff + StoreManager + Administrator); view-only
/// (export is deferred, which is what keeps Staff read-only).
/// </summary>
[ApiController]
[Route("api/v1/audit-logs")]
public sealed class AuditLogsController : ControllerBase
{
    private readonly IAuditQueryService _audit;

    public AuditLogsController(IAuditQueryService audit)
    {
        _audit = audit;
    }

    /// <summary>Searches the audit trail (newest first), paged.</summary>
    [HttpGet]
    [Authorize(Policy = Roles.Policies.AuditView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AuditLogDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Search([FromQuery] AuditLogListQuery query, CancellationToken ct)
    {
        PagedResult<AuditLogDto> result = await _audit.SearchAsync(query, ct);
        return Ok(ApiResponse<PagedResult<AuditLogDto>>.Ok(result));
    }
}
