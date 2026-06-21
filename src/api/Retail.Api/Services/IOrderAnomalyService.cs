namespace Retail.Api.Services;

/// <summary>
/// Scores orders against the three anomaly rules (REQUIREMENTS §10.1) and writes
/// <see cref="Domain.Entities.OrderAnomaly"/> flags. Phase 5B.
/// </summary>
public interface IOrderAnomalyService
{
    /// <summary>
    /// Scans recently-placed paid orders that aren't already flagged and writes one anomaly row per
    /// order that trips a rule. Idempotent (skips orders that already have a row). Returns the number
    /// of newly-flagged orders. Driven by <c>OrderAnomalyHostedService</c> on a timer.
    /// </summary>
    Task<int> ScanAsync(CancellationToken ct = default);

    /// <summary>
    /// Evaluates a single order on demand — the Mark-Shipped guard (Phase-5B Chunk 3) calls this so a
    /// just-placed order can't ship before the periodic scan has reached it. Writes an anomaly row if
    /// the order trips a rule and isn't already flagged; a no-op if the order doesn't exist or is
    /// already flagged.
    /// </summary>
    Task EvaluateOrderAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>
    /// Acknowledges a flagged order from the Risk Queue (Staff/StoreManager), clearing the Mark-Shipped
    /// block. Idempotent. Throws <c>NotFoundException</c> if the anomaly id doesn't exist.
    /// </summary>
    Task AcknowledgeAsync(Guid anomalyId, CancellationToken ct = default);
}
