using Retail.Ml.Forecasting;

namespace Retail.Tests.Unit.Ml;

/// <summary>Unit tests for the demand forecasters (Phase 5B). Holt-Winters is pure + deterministic,
/// so assertions can be exact on controlled series.</summary>
public class DemandForecasterTests
{
    [Fact]
    public void Stub_IsDeterministic_FlatTrailingMeanWithBand()
    {
        // 20 zero-days then 14 days of 10 → trailing-14 mean = 10.
        float[] series = Enumerable.Repeat(0f, 20).Concat(Enumerable.Repeat(10f, 14)).ToArray();
        var stub = new StubDemandForecaster();

        HorizonForecast a = stub.Forecast(series, 14);
        HorizonForecast b = stub.Forecast(series, 14);

        Assert.Equal(14, a.Forecast.Count);
        Assert.All(a.Forecast, v => Assert.Equal(10d, v, 3));
        Assert.All(a.UpperBound, v => Assert.Equal(12d, v, 2)); // 10 × 1.2
        Assert.All(a.LowerBound, v => Assert.Equal(8d, v, 2));  // 10 × 0.8
        Assert.Equal(a.Forecast, b.Forecast);                  // deterministic
    }

    [Fact]
    public void HoltWinters_FlatSeries_ForecastsTheLevelWithNegligibleBand()
    {
        float[] series = Enumerable.Repeat(10f, 56).ToArray(); // 8 weeks of constant demand

        HorizonForecast h = new HoltWintersForecaster().Forecast(series, 14);

        Assert.Equal(14, h.Forecast.Count);
        Assert.All(h.Forecast, v => Assert.Equal(10d, v, 1)); // recovers the level
        DemandForecastSummary s = ForecastMath.Summarize(h);
        Assert.Equal(140d, s.TotalForecast, 0);             // 14 × 10
        Assert.True(s.UpperBound - s.LowerBound < 1d);       // a perfectly flat series → ~zero residual
    }

    [Fact]
    public void HoltWinters_TrendingSeries_ProjectsTheTrendUpward()
    {
        float[] series = Enumerable.Range(0, 56).Select(i => (float)(10 + (0.5 * i))).ToArray();

        HorizonForecast h = new HoltWintersForecaster().Forecast(series, 14);

        Assert.True(h.Forecast[^1] > h.Forecast[0], "the forecast should continue the upward trend");
        Assert.True(h.Forecast[0] > series[0], "even the 1-day-ahead forecast exceeds the series start");
        DemandForecastSummary s = ForecastMath.Summarize(h);
        Assert.True(double.IsFinite(s.TotalForecast) && s.TotalForecast > 0);
    }

    [Fact]
    public void HoltWinters_SparseSeries_ProducesFiniteNonNegativeOutput()
    {
        float[] series = new float[180];
        for (int i = 0; i < series.Length; i += 30)
        {
            series[i] = 3;
        }

        HorizonForecast h = new HoltWintersForecaster().Forecast(series, 14);

        Assert.All(h.Forecast, v => Assert.False(float.IsNaN(v) || float.IsInfinity(v)));
        DemandForecastSummary s = ForecastMath.Summarize(h);
        Assert.True(double.IsFinite(s.TotalForecast) && s.TotalForecast >= 0);
        Assert.True(double.IsFinite(s.UpperBound) && s.UpperBound >= 0);
    }

    [Fact]
    public void HoltWinters_TooShortForSeasonality_FlatMeanFallback()
    {
        float[] series = Enumerable.Repeat(5f, 10).ToArray(); // < 2 × season length (14)

        HorizonForecast h = new HoltWintersForecaster().Forecast(series, 14);

        Assert.All(h.Forecast, v => Assert.Equal(5d, v, 3));
    }
}
