using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>Read-only reporting aggregates (Phase 3 §9).</summary>
public interface IReportQueryService
{
    /// <summary>Sales grouped by day (+ a category breakdown) over <c>[from, to)</c>, counting paid orders.</summary>
    Task<SalesReportDto> GetSalesByDayAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}
