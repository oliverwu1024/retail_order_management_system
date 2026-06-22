namespace Retail.Api.Services;

/// <summary>
/// Computes per-variant demand forecasts + reorder hints and writes them as rows (REQUIREMENTS §9;
/// PHASE_5B_FORECAST_SCOPE §6/§7). Phase 5B.
/// </summary>
public interface IForecastService
{
    /// <summary>
    /// Refreshes forecasts for all active variants: for each variant with sufficient history, writes a
    /// new <see cref="Domain.Entities.DemandForecast"/> row and upserts its
    /// <see cref="Domain.Entities.ReorderHint"/>. Cold-start / too-sparse variants are skipped (no row).
    /// Returns the number of variants forecast. Driven daily by <c>ForecastRefreshHostedService</c>.
    /// </summary>
    Task<int> RefreshAsync(CancellationToken ct = default);

    /// <summary>
    /// Dismisses a reorder hint (clears it from the active list; sticks across refreshes). Idempotent.
    /// Throws <c>NotFoundException</c> if the id doesn't exist.
    /// </summary>
    Task DismissReorderHintAsync(Guid reorderHintId, CancellationToken ct = default);
}
