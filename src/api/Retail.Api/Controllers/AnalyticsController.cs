using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail.Api.Common.Constants;
using Retail.Api.Common.Models;
using Retail.Api.Common.Validation;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Services;

namespace Retail.Api.Controllers;

/// <summary>
/// Reporting + the order-anomaly Risk Queue (Phase 3 §9 / Phase 5B §7). Reports are <c>Reports.View</c>
/// (view-only); the Risk Queue read + acknowledge are <c>Anomaly.Manage</c> (Staff + StoreManager +
/// Administrator).
/// </summary>
[ApiController]
[Route("api/v1/analytics")]
public sealed class AnalyticsController : ControllerBase
{
    private readonly IReportQueryService _reports;
    private readonly IOrderAnomalyService _anomalies;
    private readonly TimeProvider _timeProvider;

    public AnalyticsController(IReportQueryService reports, IOrderAnomalyService anomalies, TimeProvider timeProvider)
    {
        _reports = reports;
        _anomalies = anomalies;
        _timeProvider = timeProvider;
    }

    /// <summary>Order-anomaly Risk Queue: unacknowledged flagged orders, newest first, paged. Staff+.</summary>
    [HttpGet("anomalies")]
    [Authorize(Policy = Roles.Policies.AnomalyManage)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AnomalyDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Anomalies([FromQuery] RiskQueueQuery query, CancellationToken ct)
    {
        PagedResult<AnomalyDto> result = await _reports.GetRiskQueueAsync(query.Page, query.PageSize, ct);
        return Ok(ApiResponse<PagedResult<AnomalyDto>>.Ok(result));
    }

    /// <summary>Acknowledge a flagged order so it can ship (clears the Mark-Shipped block). Staff+.</summary>
    [HttpPost("anomalies/{id:guid}/acknowledge")]
    [Authorize(Policy = Roles.Policies.AnomalyManage)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AcknowledgeAnomaly(Guid id, CancellationToken ct)
    {
        await _anomalies.AcknowledgeAsync(id, ct);
        return Ok(ApiResponse<object?>.Ok(null));
    }

    /// <summary>Sales grouped by day (+ a category breakdown). Defaults to the last 30 days.</summary>
    [HttpGet("sales-by-day")]
    [Authorize(Policy = Roles.Policies.ReportsView)]
    [ProducesResponseType(typeof(ApiResponse<SalesReportDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SalesByDay([FromQuery] SalesByDayQuery query, CancellationToken ct)
    {
        DateTimeOffset to = query.To ?? _timeProvider.GetUtcNow();
        DateTimeOffset from = query.From ?? to.AddDays(-30);

        // Validate the EFFECTIVE range (after defaulting), so an only-`from` request can't widen the
        // window to "now". Reject a reversed range, and cap the span to bound the in-memory aggregation.
        if (DateRangeGuard.Validate(from, to, maxSpanDays: 366) is { } invalid)
        {
            return UnprocessableEntity(invalid);
        }

        SalesReportDto report = await _reports.GetSalesByDayAsync(from, to, ct);
        return Ok(ApiResponse<SalesReportDto>.Ok(report));
    }

    /// <summary>Review-sentiment summary (avg + label distribution + per-product). StoreManager + Administrator.</summary>
    [HttpGet("sentiment-summary")]
    [Authorize(Policy = Roles.Policies.SentimentView)]
    [ProducesResponseType(typeof(ApiResponse<SentimentSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SentimentSummary(CancellationToken ct)
    {
        SentimentSummaryDto summary = await _reports.GetSentimentSummaryAsync(ct);
        return Ok(ApiResponse<SentimentSummaryDto>.Ok(summary));
    }

    /// <summary>Products whose average sentiment is below the attention threshold (avg &lt; −0.2).</summary>
    [HttpGet("products-needing-attention")]
    [Authorize(Policy = Roles.Policies.SentimentView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ProductSentimentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ProductsNeedingAttention(CancellationToken ct)
    {
        IReadOnlyList<ProductSentimentDto> products = await _reports.GetProductsNeedingAttentionAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<ProductSentimentDto>>.Ok(products));
    }
}
