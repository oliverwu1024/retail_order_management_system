namespace Retail.Ml.Forecasting;

/// <summary>
/// Pure-C# Holt-Winters additive triple-exponential-smoothing demand forecaster for
/// <c>Forecast:Mode=hw</c> (Phase 5B §6) — the $0/hermetic default. Models level + linear trend + a
/// fixed-period (weekly) seasonal cycle; the per-step prediction band comes from the in-sample residual
/// standard deviation, widening with the horizon (≈ σ·√h).
/// </summary>
/// <remarks>
/// Deterministic and dependency-free (no native libraries), so it runs identically on dev, CI, and the
/// demo — unlike ML.NET SSA, whose Intel-MKL native dependency (<c>libiomp5</c>) is absent on stock
/// Linux. Each per-day forecast/bound may be summed + collapsed via <see cref="ForecastMath.Summarize"/>
/// (which clamps ≥ 0 and quadrature-propagates the total band).
/// </remarks>
public sealed class HoltWintersForecaster : IDemandForecaster
{
    private readonly int _seasonLength;
    private readonly double _alpha; // level smoothing
    private readonly double _beta;  // trend smoothing
    private readonly double _gamma; // seasonal smoothing
    private readonly double _z;     // band z-score (1.2816 ≈ 80% central interval)

    /// <summary>Defaults: weekly seasonality (7), conservative smoothing, 80% band (z ≈ 1.2816).</summary>
    public HoltWintersForecaster(
        int seasonLength = 7, double alpha = 0.3, double beta = 0.1, double gamma = 0.3, double z = 1.2816)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seasonLength);
        _seasonLength = seasonLength;
        _alpha = alpha;
        _beta = beta;
        _gamma = gamma;
        _z = z;
    }

    /// <inheritdoc />
    public HorizonForecast Forecast(IReadOnlyList<float> series, int horizon)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(horizon);

        int m = _seasonLength;
        int n = series.Count;

        // Too short to estimate a seasonal cycle → flat trailing-mean fallback (robust on sparse data;
        // the service skips genuine cold-start variants before reaching here).
        if (n < 2 * m)
        {
            return FlatFallback(series, horizon);
        }

        // Initialise level/trend from the first two seasons, seasonal indices from the first season.
        double level = Mean(series, 0, m);
        double trend = (Mean(series, m, m) - level) / m;
        var seasonal = new double[m];
        for (int i = 0; i < m; i++)
        {
            seasonal[i] = series[i] - level;
        }

        // Smooth across the series, accumulating the one-step-ahead residuals for the band.
        double sumSquaredResidual = 0;
        int residualCount = 0;
        for (int t = m; t < n; t++)
        {
            int phase = t % m;
            double fitted = level + trend + seasonal[phase];
            double error = series[t] - fitted;
            sumSquaredResidual += error * error;
            residualCount++;

            double previousLevel = level;
            level = (_alpha * (series[t] - seasonal[phase])) + ((1 - _alpha) * (level + trend));
            trend = (_beta * (level - previousLevel)) + ((1 - _beta) * trend);
            seasonal[phase] = (_gamma * (series[t] - level)) + ((1 - _gamma) * seasonal[phase]);
        }

        double sigma = residualCount > 0 ? Math.Sqrt(sumSquaredResidual / residualCount) : 0;

        var forecast = new float[horizon];
        var lower = new float[horizon];
        var upper = new float[horizon];
        for (int h = 1; h <= horizon; h++)
        {
            double point = level + (h * trend) + seasonal[(n + h - 1) % m];
            double halfWidth = _z * sigma * Math.Sqrt(h); // prediction interval widens with the horizon
            forecast[h - 1] = (float)point;
            lower[h - 1] = (float)(point - halfWidth);
            upper[h - 1] = (float)(point + halfWidth);
        }

        return new HorizonForecast(forecast, lower, upper);
    }

    // Insufficient history for seasonality: a flat forecast at the mean, with no band.
    private static HorizonForecast FlatFallback(IReadOnlyList<float> series, int horizon)
    {
        var mean = (float)(series.Count == 0 ? 0 : Mean(series, 0, series.Count));
        var flat = new float[horizon];
        Array.Fill(flat, mean);
        return new HorizonForecast(flat, (float[])flat.Clone(), (float[])flat.Clone());
    }

    private static double Mean(IReadOnlyList<float> series, int start, int count)
    {
        if (count == 0)
        {
            return 0;
        }

        double sum = 0;
        for (int i = start; i < start + count; i++)
        {
            sum += series[i];
        }

        return sum / count;
    }
}
