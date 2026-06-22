using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Common.Abstractions;
using Retail.Api.Data;
using Retail.Api.Data.Interceptors;
using Retail.Api.Services;
using Retail.Ml.Forecasting;

// Retail.Ml.Trainer — a manual demand-forecast recompute (Phase 5B). It reuses the SAME
// ForecastService.RefreshAsync as the in-process ForecastRefreshHostedService, so there is one
// forecasting code path. A minimal DI container (no app host) wires just what the service needs;
// the connection string comes from ConnectionStrings__Default (CI/containers) or the docker-compose
// default — mirroring RetailDbContextFactory. ml-train.yml builds this; the real daily run is the
// hosted service (PHASE_5B_FORECAST_SCOPE §3.1).

string connectionString =
    Environment.GetEnvironmentVariable("ConnectionStrings__Default")
    ?? "Server=localhost,1433;Database=RetailOms;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;";

var services = new ServiceCollection();
services.AddLogging();
services.AddSingleton(TimeProvider.System);
services.AddSingleton<ICurrentUserAccessor, SystemUserAccessor>();
services.AddScoped<AuditingInterceptor>();
services.AddDbContext<RetailDbContext>((sp, options) =>
    options.UseSqlServer(connectionString)
        .AddInterceptors(sp.GetRequiredService<AuditingInterceptor>()));
services.AddOptions<ForecastSettings>(); // defaults (Mode = hw)
services.AddSingleton<IDemandForecaster, HoltWintersForecaster>();
services.AddScoped<IForecastService, ForecastService>();

await using ServiceProvider provider = services.BuildServiceProvider();

Console.WriteLine("Retail.Ml.Trainer: recomputing demand forecasts...");
await using AsyncServiceScope scope = provider.CreateAsyncScope();
IForecastService forecasts = scope.ServiceProvider.GetRequiredService<IForecastService>();
int written = await forecasts.RefreshAsync();
Console.WriteLine($"Retail.Ml.Trainer: wrote {written} variant forecast(s).");
return 0;

// The trainer is a background tool, not a user request → a "system" audit actor (null user).
internal sealed class SystemUserAccessor : ICurrentUserAccessor
{
    public string? UserId => null;
}
