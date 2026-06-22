namespace Retail.Api.Services;

/// <summary>Demand-forecasting settings, bound from the <c>Forecast</c> config section (Phase 5B).</summary>
/// <remarks>
/// Unlike <c>Ai</c>, there is no key and nothing to validate at boot — the forecaster is pure compute,
/// so <see cref="Mode"/> defaults to the real model (<c>"hw"</c>); <c>"stub"</c> is opt-in for an
/// empty-DB clone / CI / tests.
/// </remarks>
public sealed class ForecastSettings
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Forecast";

    /// <summary><c>"hw"</c> (Holt-Winters, default) or <c>"stub"</c> (flat trailing-mean, training-free).</summary>
    public string Mode { get; set; } = "hw";

    /// <summary>Supplier lead time in days for the safety-stock formula.</summary>
    public int LeadTimeDays { get; set; } = 7;

    /// <summary>Service-level z for safety stock (1.65 ≈ 95%).</summary>
    public double ServiceLevelZ { get; set; } = 1.65;

    /// <summary>Skip a variant whose order history spans fewer than this many days (cold-start).</summary>
    public int MinHistoryDays { get; set; } = 30;

    /// <summary>Skip a variant with fewer than this many non-zero demand-days in the window (too sparse).</summary>
    public int MinNonZeroDays { get; set; } = 14;

    /// <summary><c>true</c> when <see cref="Mode"/> is <c>"stub"</c> (case-insensitive).</summary>
    public bool IsStub => string.Equals(Mode, "stub", StringComparison.OrdinalIgnoreCase);
}
