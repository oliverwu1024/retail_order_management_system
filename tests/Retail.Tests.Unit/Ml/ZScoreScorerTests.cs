using Retail.Ml.Anomaly;

namespace Retail.Tests.Unit.Ml;

/// <summary>Unit tests for the pure <see cref="ZScoreScorer"/> (Phase 5B).</summary>
public class ZScoreScorerTests
{
    [Fact]
    public void Score_ComputesZAgainstSampleMeanAndPopulationStdDev()
    {
        // sample {2,4,6,8,10}: mean 6, population σ = sqrt(40/5) = 2.8284…
        double[] sample = { 2, 4, 6, 8, 10 };

        // (12 - 6) / 2.8284 = 2.1213…
        Assert.Equal(2.121, ZScoreScorer.Score(12, sample), 3);
    }

    [Fact]
    public void Score_ZeroStdDevSample_ReturnsZero()
    {
        // σ-guard: a uniform sample would divide by zero — return 0 (not anomalous) instead.
        Assert.Equal(0, ZScoreScorer.Score(100, new double[] { 5, 5, 5, 5 }));
    }

    [Fact]
    public void Score_FewerThanTwoPoints_ReturnsZero()
    {
        Assert.Equal(0, ZScoreScorer.Score(100, new double[] { 5 }));
        Assert.Equal(0, ZScoreScorer.Score(100, Array.Empty<double>()));
    }

    [Fact]
    public void Score_ClearOutlier_ProducesLargeZ()
    {
        double[] sample = { 10, 11, 9, 10, 12, 8, 10, 11 };

        Assert.True(Math.Abs(ZScoreScorer.Score(40, sample)) > 3);
    }

    [Fact]
    public void Score_ValueAtMean_IsZero()
    {
        double[] sample = { 4, 6, 8, 10, 12 }; // mean 8

        Assert.Equal(0, ZScoreScorer.Score(8, sample), 6);
    }
}
