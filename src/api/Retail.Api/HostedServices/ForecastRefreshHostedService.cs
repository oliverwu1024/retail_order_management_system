using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Retail.Api.Services;

namespace Retail.Api.HostedServices;

/// <summary>
/// Daily refresh of per-variant demand forecasts + reorder hints (REQUIREMENTS §9.2;
/// PHASE_5B_FORECAST_SCOPE §7). The in-process precursor to a Phase-8 <c>ForecastRefreshFn</c>.
/// </summary>
/// <remarks>
/// Mirrors <c>OrderAnomalyHostedService</c>: a singleton that resolves the scoped
/// <see cref="IForecastService"/> from a fresh DI scope each tick, runs a <see cref="PeriodicTimer"/>
/// on the injected <see cref="TimeProvider"/>, refreshes IMMEDIATELY on startup (so the dashboard fills
/// after a boot/deploy) and then daily, with a per-tick try/catch. The immediate refresh is why its
/// registration is gated OFF in the "Testing" environment (PHASE_5B_FORECAST_SCOPE §14) — it would
/// otherwise write rows mid-test.
/// </remarks>
public sealed class ForecastRefreshHostedService : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ForecastRefreshHostedService> _logger;

    public ForecastRefreshHostedService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<ForecastRefreshHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RefreshInterval, _timeProvider);
        try
        {
            // do/while → refresh once immediately, then daily.
            do
            {
                try
                {
                    await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                    IForecastService forecasts = scope.ServiceProvider.GetRequiredService<IForecastService>();
                    await forecasts.RefreshAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    // A failed refresh must not kill the loop — log it and retry next interval.
                    _logger.LogError(ex, "Forecast refresh failed; will retry next interval.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — the host cancelled the stopping token.
        }
    }
}
