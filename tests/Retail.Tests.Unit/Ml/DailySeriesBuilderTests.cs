using Retail.Ml.Forecasting;

namespace Retail.Tests.Unit.Ml;

/// <summary>Unit tests for the pure <see cref="DailySeriesBuilder"/> (Phase 5B).</summary>
public class DailySeriesBuilderTests
{
    [Fact]
    public void Build_ZeroFillsMissingDays_MostRecentLast()
    {
        var end = new DateOnly(2026, 6, 20);
        var demand = new Dictionary<DateOnly, int> { [end] = 3, [end.AddDays(-2)] = 5 };

        float[] series = DailySeriesBuilder.Build(demand, end, 5);

        // index 0 = end-4 … index 4 = end. Day end-2 → 5, day end → 3, rest 0.
        Assert.Equal(new float[] { 0, 0, 5, 0, 3 }, series);
    }

    [Fact]
    public void Build_EmptyDemand_AllZeros()
    {
        float[] series = DailySeriesBuilder.Build(new Dictionary<DateOnly, int>(), new DateOnly(2026, 6, 20), 4);

        Assert.Equal(new float[] { 0, 0, 0, 0 }, series);
    }

    [Fact]
    public void Build_NonPositiveLength_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DailySeriesBuilder.Build(new Dictionary<DateOnly, int>(), new DateOnly(2026, 6, 20), 0));
    }
}
