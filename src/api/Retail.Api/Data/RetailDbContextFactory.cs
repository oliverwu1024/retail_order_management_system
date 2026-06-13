using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Retail.Api.Data;

/// <summary>
/// Design-time factory that EF Core tooling (<c>dotnet ef migrations add</c>,
/// <c>dotnet ef database update</c>) uses to construct a
/// <see cref="RetailDbContext"/> WITHOUT booting the application host.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists — decouple migrations from app startup.</b> When no
/// design-time factory is present, <c>dotnet ef</c> invokes the app's entry
/// point (<c>Program.cs</c>) up to <c>builder.Build()</c> to resolve the
/// DbContext from DI. That path drags in every startup side-effect: the
/// <c>Jwt:Key</c> fail-fast, OpenTelemetry, Serilog, the full Identity stack.
/// On a fresh clone with no user-secrets and without
/// <c>ASPNETCORE_ENVIRONMENT=Development</c>, the <c>Jwt:Key</c> guard throws
/// BEFORE EF can reach the DbContext — so the documented migration command
/// fails for everyone but the original author. A design-time factory removes
/// that coupling: migrations now depend on exactly one thing, a connection
/// string, and nothing about the running application.
/// </para>
/// <para>
/// EF Core prefers an <see cref="IDesignTimeDbContextFactory{TContext}"/> over
/// the host-build path whenever one is discoverable in the target assembly.
/// A side benefit: because the host is never built at design time, the benign
/// <c>HostAbortedException</c> EF throws to stop the host (which Serilog's
/// startup try/catch logs as a scary <c>[FTL]</c> line) never occurs.
/// </para>
/// <para>
/// <b>Connection string resolution.</b> Reads the
/// <c>ConnectionStrings__Default</c> environment variable when set (CI,
/// containers, custom local setups); otherwise falls back to the local-dev SQL
/// Server that docker-compose stands up on <c>localhost:1433</c>. This string
/// is DESIGN-TIME ONLY — the running application still resolves its connection
/// string from configuration in <c>Program.cs</c>.
/// </para>
/// </remarks>
public sealed class RetailDbContextFactory : IDesignTimeDbContextFactory<RetailDbContext>
{
    // Matches docker/docker-compose.yml + appsettings.Development.json. This is
    // the well-known local-dev SA password (already in .env.example); it is not
    // a production secret and is never used by the running app.
    private const string LocalDevConnectionString =
        "Server=localhost,1433;Database=RetailOms;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;";

    public RetailDbContext CreateDbContext(string[] args)
    {
        // Env var wins (CI / containers), else the docker-compose default.
        // ASP.NET's config provider treats "__" as a ':' section separator, so
        // the same ConnectionStrings__Default name is used here and in compose.
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? LocalDevConnectionString;

        var options = new DbContextOptionsBuilder<RetailDbContext>()
            .UseSqlServer(connectionString, sql =>
                // Keep migrations in THIS assembly, mirroring the runtime
                // registration in Program.cs so tooling reads/writes the same
                // Data/Migrations/ folder. (Assembly.FullName is never null for
                // a loaded assembly, hence the null-forgiving '!'.)
                sql.MigrationsAssembly(typeof(RetailDbContext).Assembly.FullName!))
            .Options;

        // No AuditingInterceptor here: it depends on the request-scoped
        // IHttpContextAccessor (absent at design time), and migrations don't
        // exercise the SaveChanges audit path anyway.
        return new RetailDbContext(options);
    }
}
