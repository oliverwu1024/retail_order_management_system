using Microsoft.EntityFrameworkCore;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.Exceptions;
using Retail.Ml.Anomaly;

namespace Retail.Api.Services;

/// <summary>
/// In-process order-anomaly detector (REQUIREMENTS §10.1; PHASE_5B_SCOPE §6). Applies three rules
/// — a Z-score on the buyer's spend, a never-seen shipping country, and a quantity spike — and
/// writes one <see cref="OrderAnomaly"/> per flagged order.
/// </summary>
/// <remarks>
/// <para>
/// COMPUTED IN MEMORY. The shipping country lives inside the <c>ShippingAddressJson</c> value-converter
/// column, which EF can't translate to SQL, so the per-customer rules load the buyer's recent paid
/// orders and evaluate in memory rather than via a set-based query.
/// </para>
/// <para>
/// IDEMPOTENT. Candidates are paid orders placed in the recent window that don't already have an
/// anomaly row, so a re-scan never double-flags. Unflagged orders are re-evaluated each pass (a
/// buyer's baseline shifts as new orders arrive), which is correct and cheap at this scale.
/// </para>
/// </remarks>
public sealed class OrderAnomalyService : IOrderAnomalyService
{
    private const int ScanWindowDays = 14;          // orders older than this aren't retro-flagged (§3.4)
    private const int BaselineOrderCount = 50;       // per-customer baseline = last ~50 orders (§10.1)
    private const int MinCustomerBaseline = 5;       // < 5 prior → fall back to the global baseline
    private const double ZThreshold = 3.0;           // |Z| above this flags
    private const int MaxLineQuantity = 5;           // any line strictly above this flags

    private readonly RetailDbContext _db;
    private readonly TimeProvider _clock;
    private readonly ILogger<OrderAnomalyService> _logger;

    public OrderAnomalyService(RetailDbContext db, TimeProvider clock, ILogger<OrderAnomalyService> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> ScanAsync(CancellationToken ct = default)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        DateTimeOffset cutoff = now.AddDays(-ScanWindowDays);

        // Candidates: paid, recent, not yet flagged. Lines are needed for the quantity rule.
        List<Order> candidates = await _db.Orders.AsNoTracking()
            .Include(o => o.Lines)
            .Where(o => o.Status == OrderStatus.Paid
                && o.PlacedAt >= cutoff
                && !_db.OrderAnomalies.Any(a => a.OrderId == o.Id))
            .ToListAsync(ct);
        if (candidates.Count == 0)
        {
            return 0;
        }

        // Preload baselines once (avoid an N+1 across candidates).
        List<Guid> buyerIds = candidates
            .Where(c => c.CustomerProfileId.HasValue)
            .Select(c => c.CustomerProfileId!.Value)
            .Distinct()
            .ToList();

        Dictionary<Guid, List<Order>> historyByBuyer = buyerIds.Count == 0
            ? new Dictionary<Guid, List<Order>>()
            : (await _db.Orders.AsNoTracking()
                .Where(o => o.Status == OrderStatus.Paid
                    && o.CustomerProfileId != null
                    && buyerIds.Contains(o.CustomerProfileId.Value))
                .ToListAsync(ct))
                .GroupBy(o => o.CustomerProfileId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

        IReadOnlyList<(Guid Id, double Log)> globalLogTotals = await LoadGlobalLogTotalsAsync(ct);

        var newRows = new List<OrderAnomaly>();
        foreach (Order order in candidates)
        {
            IReadOnlyList<Order> history = HistoryFor(order, historyByBuyer);
            OrderAnomaly? row = Evaluate(order, history, globalLogTotals, now);
            if (row is not null)
            {
                newRows.Add(row);
            }
        }

        if (newRows.Count > 0)
        {
            _db.OrderAnomalies.AddRange(newRows);
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation(
            "Order-anomaly scan flagged {Flagged} of {Scanned} candidate order(s).", newRows.Count, candidates.Count);
        return newRows.Count;
    }

    /// <inheritdoc />
    public async Task EvaluateOrderAsync(Guid orderId, CancellationToken ct = default)
    {
        if (await _db.OrderAnomalies.AnyAsync(a => a.OrderId == orderId, ct))
        {
            return; // already evaluated/flagged — never double-flag
        }

        Order? order = await _db.Orders.AsNoTracking()
            .Include(o => o.Lines)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null)
        {
            return; // nothing to evaluate
        }

        List<Order> history = order.CustomerProfileId.HasValue
            ? await _db.Orders.AsNoTracking()
                .Where(o => o.Status == OrderStatus.Paid
                    && o.CustomerProfileId == order.CustomerProfileId
                    && o.Id != orderId)
                .ToListAsync(ct)
            : new List<Order>();

        IReadOnlyList<(Guid Id, double Log)> globalLogTotals = await LoadGlobalLogTotalsAsync(ct);

        OrderAnomaly? row = Evaluate(order, history, globalLogTotals, _clock.GetUtcNow());
        if (row is not null)
        {
            _db.OrderAnomalies.Add(row);
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <inheritdoc />
    public async Task AcknowledgeAsync(Guid anomalyId, CancellationToken ct = default)
    {
        OrderAnomaly anomaly = await _db.OrderAnomalies.FirstOrDefaultAsync(a => a.Id == anomalyId, ct)
            ?? throw new NotFoundException($"Anomaly '{anomalyId}' was not found.");
        if (anomaly.Acknowledged)
        {
            return; // idempotent
        }

        anomaly.Acknowledged = true; // UpdatedBy/UpdatedAt are stamped by the AuditingInterceptor
        await _db.SaveChangesAsync(ct);
    }

    // The pure rule evaluation — returns a row to write, or null if the order is clean.
    private static OrderAnomaly? Evaluate(
        Order order, IReadOnlyList<Order> history, IReadOnlyList<(Guid Id, double Log)> globalLogTotals, DateTimeOffset now)
    {
        var reasons = new List<string>(3);
        decimal score = 0;

        // ── Rule 1: Z-score on log(total) over the buyer's prior paid orders ──
        if (order.TotalCents > 0)
        {
            // Filter to positive totals BEFORE Take(50) so zero/credit rows can't shrink the window.
            List<double> customerLogs = history
                .Where(o => o.TotalCents > 0)
                .OrderByDescending(o => o.PlacedAt)
                .Take(BaselineOrderCount)
                .Select(o => Math.Log(o.TotalCents))
                .ToList();

            bool usingCustomer = customerLogs.Count >= MinCustomerBaseline;
            // The global fallback EXCLUDES the candidate itself, mirroring the per-customer self-exclusion:
            // a self-included population sample caps |Z| at (N−1)/√N, which is < 3 for N < 11 — Rule 1 would
            // otherwise be structurally dead on a small/fresh global pool (the cold-start case it serves).
            IReadOnlyList<double> sample = usingCustomer
                ? customerLogs
                : globalLogTotals.Where(g => g.Id != order.Id).Select(g => g.Log).ToList();

            double z = ZScoreScorer.Score(Math.Log(order.TotalCents), sample);
            if (Math.Abs(z) > ZThreshold)
            {
                score = (decimal)Math.Round(Math.Abs(z), 3);
                string scope = usingCustomer ? "this customer's usual spend" : "the typical order";
                reasons.Add(z > 0
                    ? $"Order total is far above {scope} — possible fraud, review before shipping"
                    : $"Order total is far below {scope} — worth a quick check");
            }
        }

        // ── Rule 2: a shipping country never seen on this buyer's prior orders ──
        // Only meaningful once the buyer has at least one prior order (a first order has no history).
        if (history.Count >= 1)
        {
            string country = order.ShippingAddress.Country;
            if (!string.IsNullOrWhiteSpace(country))
            {
                HashSet<string> priorCountries = history
                    .Select(o => o.ShippingAddress.Country)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (priorCountries.Count > 0 && !priorCountries.Contains(country))
                {
                    reasons.Add($"Shipping to a new country for this customer ({country}) — possible account takeover");
                }
            }
        }

        // ── Rule 3: any single line quantity above the threshold ──
        int maxQuantity = order.Lines.Count == 0 ? 0 : order.Lines.Max(l => l.Quantity);
        if (maxQuantity > MaxLineQuantity)
        {
            reasons.Add($"Unusually large quantity ({maxQuantity} of one item) — review before shipping");
        }

        if (reasons.Count == 0)
        {
            return null;
        }

        return new OrderAnomaly
        {
            OrderId = order.Id,
            Score = score,
            Reason = Truncate(string.Join("; ", reasons), 200),
            DetectedAt = now,
        };
    }

    private static IReadOnlyList<Order> HistoryFor(Order order, IReadOnlyDictionary<Guid, List<Order>> historyByBuyer)
    {
        if (order.CustomerProfileId.HasValue
            && historyByBuyer.TryGetValue(order.CustomerProfileId.Value, out List<Order>? all))
        {
            // The buyer's OTHER paid orders (exclude the order under test).
            return all.Where(o => o.Id != order.Id).ToList();
        }

        return Array.Empty<Order>();
    }

    // Returns (orderId, log(total)) for every paid order, so callers can exclude the candidate itself
    // from its own baseline (see Evaluate's self-exclusion note).
    private async Task<IReadOnlyList<(Guid Id, double Log)>> LoadGlobalLogTotalsAsync(CancellationToken ct)
    {
        var totals = await _db.Orders.AsNoTracking()
            .Where(o => o.Status == OrderStatus.Paid && o.TotalCents > 0)
            .Select(o => new { o.Id, o.TotalCents })
            .ToListAsync(ct);
        return totals.Select(t => (t.Id, Math.Log(t.TotalCents))).ToList();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
