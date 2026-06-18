using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>Read-only reporting aggregates (Phase 3 §9).</summary>
public interface IReportQueryService
{
    /// <summary>Sales grouped by day (+ a category breakdown) over <c>[from, to)</c>, counting paid orders.</summary>
    Task<SalesReportDto> GetSalesByDayAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);

    /// <summary>Review-sentiment aggregate: overall average, label distribution, and per-product averages (worst-first).</summary>
    Task<SentimentSummaryDto> GetSentimentSummaryAsync(CancellationToken ct);

    /// <summary>Products whose average sentiment is below the attention threshold (avg &lt; −0.2), worst-first.</summary>
    Task<IReadOnlyList<ProductSentimentDto>> GetProductsNeedingAttentionAsync(CancellationToken ct);
}
