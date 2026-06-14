using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Data;
using Retail.Api.Data.Interceptors;

namespace Retail.Tests.Integration;

/// <summary>
/// Boots the real <c>Retail.Api</c> pipeline in-process (TestServer) against a
/// SQLite in-memory database, for end-to-end auth tests.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why SQLite, not Testcontainers SQL Server?</b> The auth flow has no
/// SQL-Server-specific behaviour to exercise yet (no RowVersion concurrency until
/// Phase 2). SQLite-in-memory gives a real relational store with zero Docker
/// dependency, keeping the test fast and CI-trivial. Phase 2's inventory
/// concurrency tests are where Testcontainers SQL Server earns its keep.
/// </para>
/// <para>
/// <b>One open connection = one shared DB.</b> A SQLite ":memory:" database lives
/// only while a connection to it is open, and each connection gets its OWN private
/// database. We therefore open a single connection in the constructor and hand
/// that same connection to every DbContext, so the schema and rows persist across
/// requests for the lifetime of the factory.
/// </para>
/// <para>
/// <b>Schema is created in the constructor</b> (before the host builds), so the
/// app's startup seeder — which runs during host build — finds its tables and
/// seeds the four roles + the test admin successfully.
/// </para>
/// </remarks>
public sealed class AuthApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection;

    public AuthApiFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        // Pre-create the schema on the shared connection from the EF model
        // (EnsureCreated, not Migrate — migrations are SQL-Server-specific).
        var options = new DbContextOptionsBuilder<RetailDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var context = new RetailDbContext(options);
        context.Database.EnsureCreated();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" (not "Development") → user-secrets are not loaded, so the test
        // is hermetic and CI-safe. All required secrets are supplied below.
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "integration-tests-signing-key-0123456789-abcdef",
                ["Jwt:Issuer"] = "https://localhost/test",
                ["Jwt:Audience"] = "retail-oms-test",
                ["Jwt:AccessTokenMinutes"] = "15",
                ["Jwt:RefreshTokenDays"] = "14",
                ["Auth:SecureCookies"] = "false", // tests run over plain http
                ["Auth:DefaultAdmin:Email"] = "admin@test.local",
                ["Auth:DefaultAdmin:Password"] = "TestAdmin123456",
                ["Auth:DefaultAdmin:DisplayName"] = "Test Admin",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Swap the SQL Server DbContext for our shared SQLite connection.
            // EF Core 9+ makes AddDbContext ADDITIVE — each call's options action is
            // registered as an IDbContextOptionsConfiguration<T> and ALL of them run
            // when the options are built. So a second AddDbContext(UseSqlite) runs
            // alongside the app's AddDbContext(UseSqlServer), and EF rejects the two
            // providers ("Only a single database provider..."). We therefore strip the
            // app's DbContext registrations — the cached options AND the SqlServer
            // configuration action — before re-adding SQLite.
            List<ServiceDescriptor> toRemove = services.Where(descriptor =>
                    descriptor.ServiceType == typeof(DbContextOptions<RetailDbContext>)
                    || descriptor.ServiceType == typeof(DbContextOptions)
                    || (descriptor.ServiceType.IsGenericType
                        && descriptor.ServiceType.GetGenericTypeDefinition().Name
                            .StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)))
                .ToList();
            foreach (ServiceDescriptor descriptor in toRemove)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<RetailDbContext>((sp, options) =>
            {
                options.UseSqlite(_connection);
                options.AddInterceptors(sp.GetRequiredService<AuditingInterceptor>());
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
