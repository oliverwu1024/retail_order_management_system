using Microsoft.EntityFrameworkCore;
using Retail.Api.Common.Enums;
using Retail.Api.Common.Models;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>
/// Sales reporting over <see cref="Order"/>/<see cref="OrderLine"/> (Phase 3 §9). Loads the paid
/// orders in range (with their lines → category) and aggregates IN MEMORY — at portfolio scale this
/// is trivially fast and avoids an EF date-grouping translation; a SQL <c>GROUP BY</c> / indexed
/// report view is the Phase-10 optimisation if k6 ever shows a hot path.
/// </summary>
public sealed class ReportQueryService : IReportQueryService
{
    private static readonly OrderStatus[] PaidStatuses = { OrderStatus.Paid, OrderStatus.Fulfilled };

    // Bound the sentiment summary's in-memory load to a recent window (the dashboard reflects current
    // sentiment); a SQL GROUP BY is the Phase-10 optimisation if review volume ever outgrows this.
    private const int SentimentWindowDays = 365;

    private readonly RetailDbContext _db;
    private readonly TimeProvider _timeProvider;

    public ReportQueryService(RetailDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<PagedResult<AnomalyDto>> GetRiskQueueAsync(int page, int pageSize, CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Only unacknowledged flags are "in the queue" — acknowledging removes an order from it.
        IQueryable<OrderAnomaly> query = _db.OrderAnomalies.AsNoTracking().Where(a => !a.Acknowledged);
        int total = await query.CountAsync(ct);

        List<AnomalyDto> items = await query
            .OrderByDescending(a => a.DetectedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AnomalyDto(
                a.Id, a.OrderId, a.Order.OrderNumber, a.Score, a.Reason, a.DetectedAt, a.Acknowledged))
            .ToListAsync(ct);

        return new PagedResult<AnomalyDto>(items, total, page, pageSize);
    }

    /// <inheritdoc />
    public async Task<PagedResult<ForecastDto>> GetForecastsAsync(int page, int pageSize, CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Latest row per variant via a correlated subquery (a naive GroupBy-then-take-latest doesn't
        // translate). Append-per-refresh means many rows per variant; we want the most recent.
        IQueryable<DemandForecast> latest = _db.DemandForecasts.AsNoTracking()
            .Where(f => f.GeneratedAt == _db.DemandForecasts
                .Where(x => x.ProductVariantId == f.ProductVariantId)
                .Max(x => x.GeneratedAt));
        int total = await latest.CountAsync(ct);

        List<ForecastDto> items = await latest
            .OrderBy(f => f.ProductVariant.Sku)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new ForecastDto(
                f.ProductVariantId, f.ProductVariant.Sku, f.ProductVariant.Product!.Name,
                f.ForecastedQty, f.LowerBound, f.UpperBound, f.Confidence, f.GeneratedAt))
            .ToListAsync(ct);

        return new PagedResult<ForecastDto>(items, total, page, pageSize);
    }

    /// <inheritdoc />
    public async Task<PagedResult<ReorderHintDto>> GetReorderHintsAsync(int page, int pageSize, CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<ReorderHint> active = _db.ReorderHints.AsNoTracking()
            .Where(h => !h.Dismissed && h.RecommendedOrderQty > 0);
        int total = await active.CountAsync(ct);

        List<ReorderHintDto> items = await active
            .OrderByDescending(h => h.RecommendedOrderQty)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(h => new ReorderHintDto(
                h.Id, h.ProductVariantId, h.ProductVariant.Sku, h.ProductVariant.Product!.Name,
                h.RecommendedOrderQty, h.Reasoning, h.GeneratedAt))
            .ToListAsync(ct);

        return new PagedResult<ReorderHintDto>(items, total, page, pageSize);
    }

    /// <inheritdoc />
    public async Task<SalesReportDto> GetSalesByDayAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        List<Order> orders = await _db.Orders.AsNoTracking()
            .Where(o => PaidStatuses.Contains(o.Status) && o.PlacedAt >= from && o.PlacedAt < to)
            .Include(o => o.Lines)
                .ThenInclude(line => line.ProductVariant!)
                .ThenInclude(variant => variant.Product!)
                .ThenInclude(product => product.Category)
            .ToListAsync(ct);

        List<DailySalesDto> days = orders
            .GroupBy(o => DateOnly.FromDateTime(o.PlacedAt.UtcDateTime))
            .OrderBy(group => group.Key)
            .Select(group => new DailySalesDto(
                group.Key.ToString("yyyy-MM-dd"),
                group.Count(),
                group.Sum(o => (long)o.TotalCents)))
            .ToList();

        // Category breakdown = merchandise (line totals) per category. A line whose product was since
        // soft-deleted resolves to "(uncategorised)" via the global query filter.
        List<CategorySalesDto> categories = orders
            .SelectMany(o => o.Lines)
            .GroupBy(line => line.ProductVariant?.Product?.Category?.Name ?? "(uncategorised)")
            .Select(group => new CategorySalesDto(group.Key, group.Sum(line => (long)line.LineTotalCents)))
            .OrderByDescending(category => category.TotalSalesCents)
            .ToList();

        return new SalesReportDto(days, categories);
    }

    /// <inheritdoc />
    public async Task<SentimentSummaryDto> GetSentimentSummaryAsync(CancellationToken ct)
    {
        // Scored reviews in the recent window. In-memory aggregation, like sales-by-day; the window
        // bounds the load (a SQL GROUP BY is the Phase-10 optimisation).
        DateTimeOffset cutoff = _timeProvider.GetUtcNow().AddDays(-SentimentWindowDays);
        List<Review> scored = await _db.Reviews.AsNoTracking()
            .Where(r => r.CreatedAt >= cutoff && r.ProcessedAt != null && r.SentimentScore != null)
            .Include(r => r.Product)
            .ToListAsync(ct);

        double? average = scored.Count > 0
            ? Math.Round((double)scored.Average(r => r.SentimentScore!.Value), 3)
            : null;

        List<LabelCountDto> labels = scored
            .Where(r => r.SentimentLabel != null)
            .GroupBy(r => r.SentimentLabel!.Value)
            .Select(group => new LabelCountDto(group.Key.ToString(), group.Count()))
            .OrderByDescending(label => label.Count)
            .ToList();

        // Per-product average sentiment, worst-first (drives the dashboard table + the attention panel).
        List<ProductSentimentDto> products = scored
            .GroupBy(r => new { r.ProductId, Name = r.Product != null ? r.Product.Name : "(unknown)" })
            .Select(group => new ProductSentimentDto(
                group.Key.ProductId,
                group.Key.Name,
                Math.Round((double)group.Average(r => r.SentimentScore!.Value), 3),
                group.Count()))
            .OrderBy(product => product.AverageScore)
            .ToList();

        return new SentimentSummaryDto(average, scored.Count, labels, products);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProductSentimentDto>> GetProductsNeedingAttentionAsync(CancellationToken ct)
    {
        SentimentSummaryDto summary = await GetSentimentSummaryAsync(ct);
        return summary.Products.Where(product => product.AverageScore < -0.2).ToList();
    }
}
