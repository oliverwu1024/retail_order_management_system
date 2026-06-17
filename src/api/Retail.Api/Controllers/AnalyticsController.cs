using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail.Api.Common.Constants;
using Retail.Api.Common.Models;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Services;

namespace Retail.Api.Controllers;

/// <summary>
/// Reporting (Phase 3 §9). Requires the <c>Reports.View</c> policy (Staff + StoreManager +
/// Administrator, view-only).
/// </summary>
[ApiController]
[Route("api/v1/analytics")]
public sealed class AnalyticsController : ControllerBase
{
    private readonly IReportQueryService _reports;
    private readonly TimeProvider _timeProvider;

    public AnalyticsController(IReportQueryService reports, TimeProvider timeProvider)
    {
        _reports = reports;
        _timeProvider = timeProvider;
    }

    /// <summary>Sales grouped by day (+ a category breakdown). Defaults to the last 30 days.</summary>
    [HttpGet("sales-by-day")]
    [Authorize(Policy = Roles.Policies.ReportsView)]
    [ProducesResponseType(typeof(ApiResponse<SalesReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SalesByDay([FromQuery] SalesByDayQuery query, CancellationToken ct)
    {
        DateTimeOffset to = query.To ?? _timeProvider.GetUtcNow();
        DateTimeOffset from = query.From ?? to.AddDays(-30);
        SalesReportDto report = await _reports.GetSalesByDayAsync(from, to, ct);
        return Ok(ApiResponse<SalesReportDto>.Ok(report));
    }
}
