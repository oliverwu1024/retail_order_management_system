namespace Retail.Ml.Forecasting;

/// <summary>
/// Collapses a per-day <see cref="HorizonForecast"/> into a single total-over-horizon
/// <see cref="DemandForecastSummary"/>. Pure + deterministic — the covered home of the clamp +
/// band-propagation logic (the SSA fit itself is the only excluded glue).
/// </summary>
public static class ForecastMath
{
    /// <summary>
    /// Sums the (clamped) per-day point forecasts and propagates the band by QUADRATURE:
    /// every per-day forecast/upper is floored at 0 (demand can't be negative — SSA on a sparse series
    /// emits negatives), <c>TotalForecast = Σ forecastᵢ</c>, and the half-width on the total is
    /// <c>√Σ (upperᵢ − forecastᵢ)²</c> — the independent-errors propagation, far closer to the true CI
    /// of a sum than naively summing the per-day bounds. The lower bound is floored at 0.
    /// </summary>
    public static DemandForecastSummary Summarize(HorizonForecast horizon)
    {
        ArgumentNullException.ThrowIfNull(horizon);

        double total = 0;
        double sumSquaredHalfWidth = 0;
        for (int i = 0; i < horizon.Forecast.Count; i++)
        {
            double forecast = Math.Max(0, horizon.Forecast[i]);
            double upper = Math.Max(0, horizon.UpperBound[i]);
            double halfWidth = Math.Max(0, upper - forecast);

            total += forecast;
            sumSquaredHalfWidth += halfWidth * halfWidth;
        }

        double bandHalfWidth = Math.Sqrt(sumSquaredHalfWidth);
        return new DemandForecastSummary(total, Math.Max(0, total - bandHalfWidth), total + bandHalfWidth);
    }
}
