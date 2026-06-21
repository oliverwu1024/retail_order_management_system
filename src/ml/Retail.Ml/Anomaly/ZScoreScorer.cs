namespace Retail.Ml.Anomaly;

/// <summary>
/// Computes a Z-score of a value against a sample, with the numerical-safety guards from ADR-0003:
/// a degenerate sample (too few points, or a near-zero standard deviation) yields <c>0</c>
/// ("not anomalous") rather than a divide-by-zero or a <c>NaN</c>/<c>Infinity</c>.
/// </summary>
/// <remarks>
/// Pure + deterministic — no EF, no ML.NET, no I/O — so it unit-tests in isolation and is the
/// shared scoring primitive for order-anomaly detection (Phase 5B) and, later, fraud (ADR-0003).
/// Callers handle any domain transform (order-total rules pass <c>log(total)</c>) before scoring.
/// </remarks>
public static class ZScoreScorer
{
    /// <summary>
    /// The Z-score of <paramref name="value"/> against the population mean and standard deviation of
    /// <paramref name="sample"/>.
    /// </summary>
    /// <param name="value">The observation to score.</param>
    /// <param name="sample">The baseline population to score against.</param>
    /// <param name="epsilon">
    /// Standard-deviation floor: below this the sample is treated as degenerate and the score is
    /// <c>0</c>. Guards a uniform or near-empty baseline from producing a spurious/infinite score.
    /// </param>
    /// <returns>
    /// The signed Z-score, or <c>0</c> when the sample has fewer than two points or a standard
    /// deviation below <paramref name="epsilon"/>.
    /// </returns>
    public static double Score(double value, IReadOnlyList<double> sample, double epsilon = 1e-6)
    {
        if (sample.Count < 2)
        {
            return 0;
        }

        double mean = 0;
        for (int i = 0; i < sample.Count; i++)
        {
            mean += sample[i];
        }

        mean /= sample.Count;

        double sumSquares = 0;
        for (int i = 0; i < sample.Count; i++)
        {
            double delta = sample[i] - mean;
            sumSquares += delta * delta;
        }

        double stdDev = Math.Sqrt(sumSquares / sample.Count); // population σ
        return stdDev < epsilon ? 0 : (value - mean) / stdDev;
    }
}
