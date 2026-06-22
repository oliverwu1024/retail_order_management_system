namespace Retail.Ml.Forecasting;

/// <summary>
/// Forecasts a variant's near-term demand from its zero-filled daily-demand series (Phase 5B). The
/// <c>Forecast:Mode</c> seam: <see cref="HoltWintersForecaster"/> (the real, pure-C# model) vs
/// <see cref="StubDemandForecaster"/> (deterministic, training-free), selected at DI.
/// </summary>
public interface IDemandForecaster
{
    /// <summary>
    /// Forecasts the next <paramref name="horizon"/> days from <paramref name="series"/> (most-recent-last).
    /// Returns the raw per-day forecasts + confidence bounds; collapse them with
    /// <see cref="ForecastMath.Summarize"/> (which clamps ≥ 0 and propagates the total band).
    /// </summary>
    HorizonForecast Forecast(IReadOnlyList<float> series, int horizon);
}
