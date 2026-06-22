namespace Retail.Ml.Forecasting;

/// <summary>
/// Builds a continuous, zero-filled daily-demand series from sparse per-day observations (Phase 5B).
/// Pure + deterministic — no EF, no ML.NET — so it unit-tests in isolation; the forecaster consumes
/// the fixed-length <c>float[]</c> it produces.
/// </summary>
public static class DailySeriesBuilder
{
    /// <summary>
    /// Returns a <paramref name="length"/>-element series ending (inclusive) on
    /// <paramref name="windowEndInclusive"/>, most-recent-last: index <c>length-1</c> is that day's
    /// demand and index 0 is <c>length-1</c> days earlier. Days absent from <paramref name="demandByDay"/>
    /// are zero (no sale that day).
    /// </summary>
    /// <param name="demandByDay">Observed demand keyed by day (missing days = 0).</param>
    /// <param name="windowEndInclusive">The last (most recent) day in the series.</param>
    /// <param name="length">The series length (e.g. the forecaster's train size, 180).</param>
    public static float[] Build(IReadOnlyDictionary<DateOnly, int> demandByDay, DateOnly windowEndInclusive, int length)
    {
        ArgumentNullException.ThrowIfNull(demandByDay);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        var series = new float[length];
        DateOnly start = windowEndInclusive.AddDays(-(length - 1));
        for (int i = 0; i < length; i++)
        {
            series[i] = demandByDay.TryGetValue(start.AddDays(i), out int qty) ? qty : 0f;
        }

        return series;
    }
}
