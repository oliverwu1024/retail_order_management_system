using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// A per-variant demand forecast (DATABASE_DESIGN §3.17) — Phase 5B (forecasting).
/// </summary>
/// <remarks>
/// <para>
/// SYSTEM-GENERATED, APPENDED PER REFRESH. Written by the daily <c>ForecastService</c> /
/// <c>ForecastRefreshHostedService</c> (PHASE_5B_FORECAST_SCOPE): for each active variant it fits an
/// ML.NET SSA model on the 180-day daily-demand series and stores the 14-day-ahead outlook. A new row
/// is appended each run (history retained); the latest is read via
/// <c>IX_DemandForecast_ProductVariantId_GeneratedAt</c>.
/// </para>
/// <para>
/// <see cref="ForecastedQty"/> is the 14-day TOTAL (sum of the clamped per-day forecasts);
/// <see cref="LowerBound"/>/<see cref="UpperBound"/> are an 80% prediction band on that total,
/// quadrature-propagated and floored at 0 (PHASE_5B_FORECAST_SCOPE §3.4) — not a naive sum of per-day
/// bounds. <see cref="Confidence"/> is a data-sufficiency proxy (history backing the forecast), not a
/// calibrated statistical confidence. Variants with too little history are skipped (no row).
/// </para>
/// <para>
/// NOT AUDIT-MONITORED (high-volume, system-generated — like <see cref="Review"/>/<see cref="OrderAnomaly"/>);
/// the <see cref="IAuditableEntity"/> stamps still apply via <c>AuditingInterceptor</c>.
/// </para>
/// </remarks>
public class DemandForecast : IAuditableEntity
{
    /// <summary>Surrogate PK (EF-generated sequential GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the forecast's <see cref="ProductVariant"/>. Required.</summary>
    public Guid ProductVariantId { get; set; }

    /// <summary>Navigation to the variant.</summary>
    public ProductVariant ProductVariant { get; set; } = null!;

    /// <summary>Forecast horizon in days (14).</summary>
    public short Horizon { get; set; }

    /// <summary>Total predicted demand over the horizon (sum of clamped per-day forecasts). <c>decimal(10,2)</c>.</summary>
    public decimal ForecastedQty { get; set; }

    /// <summary>Lower bound of the 80% prediction band on the total (quadrature, floored at 0). <c>decimal(10,2)</c>.</summary>
    public decimal LowerBound { get; set; }

    /// <summary>Upper bound of the 80% prediction band on the total. <c>decimal(10,2)</c>.</summary>
    public decimal UpperBound { get; set; }

    /// <summary>Data-sufficiency proxy in 0..1 (history backing the forecast), not a calibrated confidence. <c>decimal(4,3)</c>.</summary>
    public decimal Confidence { get; set; }

    /// <summary>Identifier of the run that produced this row (ISO date, or "stub"). ≤ 40 chars.</summary>
    public string ModelVersion { get; set; } = string.Empty;

    /// <summary>When this forecast was produced (UTC), service-stamped via <c>TimeProvider</c>.</summary>
    public DateTimeOffset GeneratedAt { get; set; }

    // ── IAuditableEntity (stamped by AuditingInterceptor) ────────────────────
    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }
    /// <inheritdoc />
    public string? CreatedBy { get; set; }
    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }
    /// <inheritdoc />
    public string? UpdatedBy { get; set; }
}
