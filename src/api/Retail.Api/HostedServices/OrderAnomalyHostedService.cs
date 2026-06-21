using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Retail.Api.Services;

namespace Retail.Api.HostedServices;

/// <summary>
/// Periodically runs <see cref="IOrderAnomalyService.ScanAsync"/> to flag anomalous orders for the
/// Risk Queue (REQUIREMENTS §10.1; PHASE_5B_SCOPE §6). The in-process precursor to the Phase-8
/// <c>OrderAnomalyScanFn</c> Function.
/// </summary>
/// <remarks>
/// A hosted service is a SINGLETON, so it resolves the scoped, DbContext-backed
/// <see cref="IOrderAnomalyService"/> from a fresh DI scope each tick. The <see cref="PeriodicTimer"/>
/// is built on the injected <see cref="TimeProvider"/>, and a per-tick try/catch keeps one failed
/// scan from tearing down the loop. It scans IMMEDIATELY on startup (so the Risk Queue is populated
/// promptly after a boot/deploy) and then every <see cref="ScanInterval"/> — which is why its
/// registration is gated OFF in the "Testing" environment (PHASE_5B_SCOPE §14): an immediate scan
/// would otherwise flag orders other integration tests seed.
/// </remarks>
public sealed class OrderAnomalyHostedService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrderAnomalyHostedService> _logger;

    public OrderAnomalyHostedService(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<OrderAnomalyHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ScanInterval, _timeProvider);
        try
        {
            // do/while → scan once immediately, then on every tick.
            do
            {
                try
                {
                    await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                    IOrderAnomalyService scanner = scope.ServiceProvider.GetRequiredService<IOrderAnomalyService>();
                    await scanner.ScanAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    // A failed scan must not kill the loop — log it and retry next interval.
                    _logger.LogError(ex, "Order-anomaly scan failed; will retry next interval.");
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
