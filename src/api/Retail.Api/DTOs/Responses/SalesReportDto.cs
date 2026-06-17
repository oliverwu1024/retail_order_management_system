namespace Retail.Api.DTOs.Responses;

/// <summary>Sales-by-day report (Phase 3 §9): per-day totals (the chart) + a category breakdown over
/// the range. Money is integer cents (the client formats it).</summary>
public sealed record SalesReportDto(
    IReadOnlyList<DailySalesDto> Days,
    IReadOnlyList<CategorySalesDto> Categories);

/// <summary>One day's sales. <see cref="Date"/> is an ISO <c>yyyy-MM-dd</c> (UTC) day.</summary>
public sealed record DailySalesDto(string Date, int OrderCount, long TotalSalesCents);

/// <summary>Total merchandise (line totals) per category over the range.</summary>
public sealed record CategorySalesDto(string Category, long TotalSalesCents);
