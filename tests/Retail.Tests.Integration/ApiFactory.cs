using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Retail.Api.Data;
using Testcontainers.Azurite;
using Testcontainers.MsSql;

namespace Retail.Tests.Integration;

/// <summary>
/// Boots the real <c>Retail.Api</c> pipeline in-process (TestServer) against a real
/// SQL Server in a throwaway Testcontainers container — production-identical, per
/// CODING_STANDARDS. Shared by every integration test class (auth, catalog, ...).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a real SQL Server, not SQLite.</b> The app ships on SQL Server; SQLite is
/// a different engine with real behavioural gaps (e.g. it can't translate a
/// <c>DateTimeOffset</c> comparison, and has no <c>rowversion</c> or filtered
/// indexes). Testing on the engine we deploy on removes the "passes on SQLite,
/// behaves differently on SQL Server" risk — at the cost of a container spin-up and
/// a Docker dependency at test time (CI runners and this dev box both have Docker).
/// </para>
/// <para>
/// <b>How the DB is wired.</b> We do NOT swap the DbContext registration. We start
/// the container, then override <c>ConnectionStrings:Default</c> so the app's own
/// <c>UseSqlServer(...)</c> points at it — the production code path, unchanged. The
/// schema is built with the REAL EF migrations (<c>MigrateAsync</c>) before the
/// host's startup seeder runs, so the seeder finds its tables and populates the four
/// roles + the test admin.
/// </para>
/// </remarks>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    // Reuse the image docker-compose already pulls, so the container starts from
    // cache rather than a fresh pull. (Image passed to the ctor — the parameterless
    // MsSqlBuilder() is obsolete as of Testcontainers 4.12.)
    private readonly MsSqlContainer _sql =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    // Azurite for the product-image upload path (Task 1.2.8).
    private readonly AzuriteContainer _azurite =
        new AzuriteBuilder("mcr.microsoft.com/azure-storage/azurite:latest").Build();

    private string _connectionString = string.Empty;
    private string _blobConnectionString = string.Empty;

    /// <summary>xUnit calls this once before the class's tests: start SQL Server, migrate the schema.</summary>
    public async Task InitializeAsync()
    {
        await Task.WhenAll(_sql.StartAsync(), _azurite.StartAsync());

        _blobConnectionString = _azurite.GetConnectionString();

        // Target a dedicated database (not master) on the throwaway container.
        _connectionString = new SqlConnectionStringBuilder(_sql.GetConnectionString())
        {
            InitialCatalog = "RetailOmsTests",
        }.ConnectionString;

        // Apply the REAL migrations (EF creates the database if absent). This is the
        // production schema, so the startup seeder that runs when the host builds
        // finds its tables.
        var options = new DbContextOptionsBuilder<RetailDbContext>()
            .UseSqlServer(_connectionString)
            .Options;
        await using var context = new RetailDbContext(options);
        await context.Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" (not "Development") → user-secrets are not loaded, so the test is
        // hermetic and CI-safe. All required secrets are supplied below.
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Point the app's own UseSqlServer at the container — no DbContext swap.
                ["ConnectionStrings:Default"] = _connectionString,
                ["Jwt:Key"] = "integration-tests-signing-key-0123456789-abcdef",
                ["Jwt:Issuer"] = "https://localhost/test",
                ["Jwt:Audience"] = "retail-oms-test",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"] = "14",
                ["Auth:SecureCookies"] = "false", // tests run over plain http
                ["Auth:DefaultAdmin:Email"] = "admin@test.local",
                ["Auth:DefaultAdmin:Password"] = "TestAdmin123456",
                ["Auth:DefaultAdmin:DisplayName"] = "Test Admin",
                ["Storage:ConnectionString"] = _blobConnectionString,
                ["Storage:ProductImagesContainer"] = "product-images",
            });
        });
    }

    /// <summary>
    /// xUnit's async teardown. Implemented EXPLICITLY because the base
    /// <see cref="WebApplicationFactory{TEntryPoint}"/> already declares a
    /// <c>ValueTask DisposeAsync()</c>, which would collide with this <c>Task</c>-returning one.
    /// </summary>
    async Task IAsyncLifetime.DisposeAsync()
    {
        await _sql.DisposeAsync();
        await _azurite.DisposeAsync();
        await base.DisposeAsync();
    }
}
