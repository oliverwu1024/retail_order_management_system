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
