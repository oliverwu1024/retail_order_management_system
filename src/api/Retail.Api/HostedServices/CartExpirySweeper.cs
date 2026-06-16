using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Retail.Api.Services;

namespace Retail.Api.HostedServices;

/// <summary>
/// Periodically runs <see cref="ICartSweepService"/> to abandon expired carts and return
/// their held stock (Story 2.3).
/// </summary>
/// <remarks>
/// A hosted service is a SINGLETON, so it can't take a scoped, DbContext-backed service in its
/// constructor — it resolves <see cref="ICartSweepService"/> from a fresh DI scope each tick
/// instead. The <see cref="PeriodicTimer"/> is built on the injected <see cref="TimeProvider"/>
/// so the cadence is test-controllable, and a per-tick try/catch ensures one failed sweep
/// never tears down the loop.
/// </remarks>
public sealed class CartExpirySweeper : BackgroundService
{
    // Scan cadence — distinct from the 15-min reservation TTL and the 30-min cart lifetime.
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CartExpirySweeper> _logger;

    public CartExpirySweeper(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<CartExpirySweeper> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, _timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
                    ICartSweepService sweeper = scope.ServiceProvider.GetRequiredService<ICartSweepService>();
                    await sweeper.SweepExpiredCartsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    // A failed sweep must not kill the loop — log it and retry next interval.
                    _logger.LogError(ex, "Cart expiry sweep failed; will retry next interval.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — the host cancelled the stopping token.
        }
    }
}
