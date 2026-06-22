using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.Exceptions;
using Retail.Ml.Forecasting;

namespace Retail.Api.Services;

/// <summary>
/// Builds each active variant's daily-demand series, forecasts it (Holt-Winters or stub), and writes a
/// <see cref="DemandForecast"/> + upserts a <see cref="ReorderHint"/> (PHASE_5B_FORECAST_SCOPE §6/§7).
/// </summary>
/// <remarks>
/// The per-day demand series is grouped + zero-filled <b>in memory</b> (EF can't translate a
/// day-of-<c>PlacedAt</c> grouping — the same trap <c>ReportQueryService.GetSalesByDayAsync</c>
/// handles in memory). Forecasts append (history retained); reorder hints upsert one row per variant.
/// </remarks>
public sealed class ForecastService : IForecastService
{
    private const int SeriesDays = 180; // daily-demand window (the forecaster's train size)
    private const int Horizon = 14;     // forecast horizon (days)
    private static readonly OrderStatus[] PaidStatuses = { OrderStatus.Paid, OrderStatus.Fulfilled };

    private readonly RetailDbContext _db;
    private readonly IDemandForecaster _forecaster;
    private readonly TimeProvider _clock;
    private readonly ForecastSettings _settings;
    private readonly ILogger<ForecastService> _logger;

    public ForecastService(
        RetailDbContext db,
        IDemandForecaster forecaster,
        TimeProvider clock,
        IOptions<ForecastSettings> settings,
        ILogger<ForecastService> logger)
    {
        _db = db;
        _forecaster = forecaster;
        _clock = clock;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> RefreshAsync(CancellationToken ct = default)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);
        // Midnight UTC of the earliest series day — matches DailySeriesBuilder's [today-(SeriesDays-1), today]
        // window exactly (a plain now.AddDays(-SeriesDays) would pull one extra boundary day the builder drops).
        DateTimeOffset windowStart = new(today.AddDays(-(SeriesDays - 1)).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        string modelVersion = _settings.IsStub ? "stub" : today.ToString("yyyy-MM-dd");

        // Active variants of non-deleted products. Skipping soft-deleted-product variants here keeps the
        // writes consistent with the reads (which inner-join Product's !IsDeleted filter) — no dead rows.
        List<ProductVariant> variants = await _db.ProductVariants.AsNoTracking()
            .Include(v => v.Inventory)
            .Where(v => v.IsActive && !v.Product!.IsDeleted)
            .ToListAsync(ct);
        if (variants.Count == 0)
        {
            return 0;
        }

        List<Guid> variantIds = variants.Select(v => v.Id).ToList();

        // Raw (variant, placedAt, qty) rows for paid/fulfilled lines in the window — grouped in memory.
        var lineRows = await _db.OrderLines.AsNoTracking()
            .Where(l => variantIds.Contains(l.ProductVariantId)
                && PaidStatuses.Contains(l.Order!.Status)
                && l.Order.PlacedAt >= windowStart)
            .Select(l => new { l.ProductVariantId, l.Order!.PlacedAt, l.Quantity })
            .ToListAsync(ct);

        Dictionary<Guid, Dictionary<DateOnly, int>> demandByVariant = lineRows
            .GroupBy(r => r.ProductVariantId)
            .ToDictionary(
                byVariant => byVariant.Key,
                byVariant => byVariant
                    .GroupBy(r => DateOnly.FromDateTime(r.PlacedAt.UtcDateTime))
                    .ToDictionary(byDay => byDay.Key, byDay => byDay.Sum(r => r.Quantity)));

        // Existing reorder hints (TRACKED — we upsert in place, preserving Dismissed). Grouped tolerantly
        // (First per variant) rather than ToDictionary so a stray duplicate row — possible only under the
        // documented Phase-8 multi-writer race, since there's no UNIQUE(ProductVariantId) yet — degrades to
        // "update one, leave the dup" instead of throwing and bricking every future refresh (review §17).
        Dictionary<Guid, ReorderHint> existingHints = (await _db.ReorderHints
                .Where(h => variantIds.Contains(h.ProductVariantId))
                .ToListAsync(ct))
            .GroupBy(h => h.ProductVariantId)
            .ToDictionary(g => g.Key, g => g.First());

        int forecast = 0;
        foreach (ProductVariant variant in variants)
        {
            if (!demandByVariant.TryGetValue(variant.Id, out Dictionary<DateOnly, int>? demandByDay)
                || demandByDay.Count == 0)
            {
                continue; // no sales in the window → nothing to forecast
            }

            // Cold-start / too-sparse skip (§3.6).
            int spanDays = (today.DayNumber - demandByDay.Keys.Min().DayNumber) + 1;
            if (spanDays < _settings.MinHistoryDays || demandByDay.Count < _settings.MinNonZeroDays)
            {
                continue;
            }

            float[] series = DailySeriesBuilder.Build(demandByDay, today, SeriesDays);
            DemandForecastSummary summary = ForecastMath.Summarize(_forecaster.Forecast(series, Horizon));
            decimal confidence = (decimal)Math.Clamp(spanDays / (double)SeriesDays, 0, 1);

            _db.DemandForecasts.Add(new DemandForecast
            {
                ProductVariantId = variant.Id,
                Horizon = Horizon,
                ForecastedQty = (decimal)summary.TotalForecast,
                LowerBound = (decimal)summary.LowerBound,
                UpperBound = (decimal)summary.UpperBound,
                Confidence = confidence,
                ModelVersion = modelVersion,
                GeneratedAt = now,
            });

            // Reorder: safety stock from the series σ (intermittent-demand caveat — §17), then upsert.
            double sigma = PopulationStdDev(series);
            int safetyStock = (int)Math.Ceiling(_settings.ServiceLevelZ * sigma * Math.Sqrt(_settings.LeadTimeDays));
            int onHand = variant.Inventory?.OnHand ?? 0;
            int forecast14d = (int)Math.Ceiling(summary.TotalForecast);
            int recommended = Math.Max(0, forecast14d + safetyStock - onHand);
            string reasoning = $"14-day demand {forecast14d} + safety {safetyStock}, on-hand {onHand}";

            if (existingHints.TryGetValue(variant.Id, out ReorderHint? hint))
            {
                hint.RecommendedOrderQty = recommended;
                hint.Reasoning = reasoning;
                hint.GeneratedAt = now; // Dismissed left as-is (sticks)
            }
            else
            {
                _db.ReorderHints.Add(new ReorderHint
                {
                    ProductVariantId = variant.Id,
                    RecommendedOrderQty = recommended,
                    Reasoning = reasoning,
                    GeneratedAt = now,
                });
            }

            forecast++;
        }

        if (forecast > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Forecast refresh wrote {Forecast} of {Active} active variant(s).", forecast, variants.Count);
        return forecast;
    }

    /// <inheritdoc />
    public async Task DismissReorderHintAsync(Guid reorderHintId, CancellationToken ct = default)
    {
        ReorderHint hint = await _db.ReorderHints.FirstOrDefaultAsync(h => h.Id == reorderHintId, ct)
            ?? throw new NotFoundException($"Reorder hint '{reorderHintId}' was not found.");
        if (hint.Dismissed)
        {
            return; // idempotent
        }

        hint.Dismissed = true; // UpdatedBy/UpdatedAt stamped by the AuditingInterceptor
        await _db.SaveChangesAsync(ct);
    }

    private static double PopulationStdDev(IReadOnlyList<float> series)
    {
        if (series.Count == 0)
        {
            return 0;
        }

        double mean = 0;
        for (int i = 0; i < series.Count; i++)
        {
            mean += series[i];
        }

        mean /= series.Count;

        double sumSquares = 0;
        for (int i = 0; i < series.Count; i++)
        {
            double delta = series[i] - mean;
            sumSquares += delta * delta;
        }

        return Math.Sqrt(sumSquares / series.Count);
    }
}
