namespace Retail.Ml.Forecasting;

/// <summary>
/// The raw per-day output of an <see cref="IDemandForecaster"/> over the horizon — each list has
/// length = horizon. Values may be negative (a band that dips below 0 on a sparse series);
/// <see cref="ForecastMath.Summarize"/> clamps and collapses them.
/// </summary>
/// <param name="Forecast">Per-day point forecasts.</param>
/// <param name="LowerBound">Per-day lower confidence bounds.</param>
/// <param name="UpperBound">Per-day upper confidence bounds.</param>
public sealed record HorizonForecast(
    IReadOnlyList<float> Forecast,
    IReadOnlyList<float> LowerBound,
    IReadOnlyList<float> UpperBound);

/// <summary>
/// The horizon collapsed to a single total-over-horizon outlook: the summed point forecast plus an
/// 80% prediction band on that total (quadrature-propagated, floored at 0). What a
/// <c>DemandForecast</c> row stores.
/// </summary>
/// <param name="TotalForecast">Σ of the clamped per-day forecasts (≥ 0).</param>
/// <param name="LowerBound">Lower band on the total (≥ 0).</param>
/// <param name="UpperBound">Upper band on the total.</param>
public sealed record DemandForecastSummary(double TotalForecast, double LowerBound, double UpperBound);
