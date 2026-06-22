using Retail.Ml.Forecasting;

namespace Retail.Tests.Unit.Ml;

/// <summary>Unit tests for the pure <see cref="ForecastMath"/> clamp + quadrature-band aggregation (Phase 5B).</summary>
public class ForecastMathTests
{
    [Fact]
    public void Summarize_ClampsNegatives_SumsForecast_PropagatesBandByQuadrature()
    {
        // forecast [2,-1,3] → clamped [2,0,3], total 5. upper [4,1,5]; half-widths [2,1,2] → √(4+1+4)=3.
        var horizon = new HorizonForecast(
            new float[] { 2, -1, 3 }, new float[] { 0, 0, 0 }, new float[] { 4, 1, 5 });

        DemandForecastSummary s = ForecastMath.Summarize(horizon);

        Assert.Equal(5d, s.TotalForecast, 6);
        Assert.Equal(2d, s.LowerBound, 6); // max(0, 5 − 3)
        Assert.Equal(8d, s.UpperBound, 6); // 5 + 3
    }

    [Fact]
    public void Summarize_FloorsLowerBoundAtZero()
    {
        // total 1, upper 5 → half-width 4 → lower max(0, 1−4) = 0.
        var horizon = new HorizonForecast(new float[] { 1 }, new float[] { 0 }, new float[] { 5 });

        DemandForecastSummary s = ForecastMath.Summarize(horizon);

        Assert.Equal(1d, s.TotalForecast, 6);
        Assert.Equal(0d, s.LowerBound, 6);
        Assert.Equal(5d, s.UpperBound, 6);
    }

    [Fact]
    public void Summarize_NotSimplySumOfPerDayBounds()
    {
        // 4 days, each half-width 3 → naive sum would be 12; quadrature = √(4·9) = 6.
        var horizon = new HorizonForecast(
            new float[] { 1, 1, 1, 1 }, new float[] { 0, 0, 0, 0 }, new float[] { 4, 4, 4, 4 });

        DemandForecastSummary s = ForecastMath.Summarize(horizon);

        Assert.Equal(4d, s.TotalForecast, 6);
        Assert.Equal(10d, s.UpperBound, 6); // 4 + 6 (quadrature), NOT 4 + 12
    }
}
