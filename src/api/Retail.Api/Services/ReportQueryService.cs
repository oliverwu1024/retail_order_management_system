using Microsoft.EntityFrameworkCore;
using Retail.Api.Common.Enums;
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

    private readonly RetailDbContext _db;

    public ReportQueryService(RetailDbContext db)
    {
        _db = db;
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
}
