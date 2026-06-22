namespace Retail.Ml.Forecasting;

/// <summary>
/// Deterministic, training-free forecaster for <c>Forecast:Mode=stub</c> (Phase 5B §3.3): a flat
/// forecast at the trailing-mean of recent demand with a fixed ±20% band. Lets a fresh/empty clone,
/// CI, and tests produce reproducible forecasts with no ML.NET fit.
/// </summary>
public sealed class StubDemandForecaster : IDemandForecaster
{
    private const int TrailingDays = 14;
    private const float BandFraction = 0.20f;

    /// <inheritdoc />
    public HorizonForecast Forecast(IReadOnlyList<float> series, int horizon)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(horizon);

        // Trailing-mean over the most recent days (or the whole series if shorter).
        int tail = Math.Min(series.Count, TrailingDays);
        double sum = 0;
        for (int i = series.Count - tail; i < series.Count; i++)
        {
            sum += series[i];
        }

        float mean = tail == 0 ? 0f : (float)(sum / tail);

        var forecast = new float[horizon];
        var lower = new float[horizon];
        var upper = new float[horizon];
        for (int i = 0; i < horizon; i++)
        {
            forecast[i] = mean;
            lower[i] = mean * (1f - BandFraction);
            upper[i] = mean * (1f + BandFraction);
        }

        return new HorizonForecast(forecast, lower, upper);
    }
}
