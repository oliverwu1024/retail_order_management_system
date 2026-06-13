using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Retail.Api.Common.Abstractions;
using Retail.Api.Data.Interceptors;
using Retail.Api.Domain.Common;

namespace Retail.Tests.Unit.Data;

/// <summary>
/// Unit tests for <see cref="AuditingInterceptor"/>.
/// </summary>
/// <remarks>
/// Uses a <b>SQLite in-memory</b> database rather than the EF Core InMemory
/// provider on purpose: InMemory is not a real relational store and would not
/// faithfully exercise the per-column <c>IsModified = false</c> protection — the
/// most important behavior here. SQLite generates real UPDATE SQL, so the
/// "created fields are not overwritten" test actually proves something.
///
/// No production entity implements <see cref="IAuditableEntity"/> yet, so the
/// tests run against a throwaway <see cref="AuditableThing"/> + a local
/// <see cref="TestDbContext"/>. The interceptor's two collaborators
/// (<see cref="ICurrentUserAccessor"/>, <see cref="TimeProvider"/>) are replaced
/// with tiny fakes — no ASP.NET framework needed, which is the payoff of the
/// ICurrentUserAccessor abstraction.
/// </remarks>
public class AuditingInterceptorTests
{
    // A fixed instant so every assertion is deterministic.
    private static readonly DateTimeOffset Now = new(2026, 6, 14, 9, 0, 0, TimeSpan.Zero);

    // ── test doubles ─────────────────────────────────────────────────────────

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubCurrentUser(string? userId) : ICurrentUserAccessor
    {
        public string? UserId { get; } = userId;
    }

    // ── throwaway auditable entity + context ─────────────────────────────────

    private sealed class AuditableThing : IAuditableEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options)
        : DbContext(options)
    {
        public DbSet<AuditableThing> Things => Set<AuditableThing>();
    }

    /// <summary>
    /// Builds an isolated SQLite in-memory database with the interceptor wired in.
    /// The caller owns the returned context + connection and must dispose both —
    /// a SQLite ":memory:" database lives only while its connection is open.
    /// </summary>
    private static (TestDbContext Context, SqliteConnection Connection) NewContext(string? userId)
    {
        var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new AuditingInterceptor(new StubCurrentUser(userId), new FixedClock(Now)))
            .Options;

        var context = new TestDbContext(options);
        context.Database.EnsureCreated();
        return (context, connection);
    }

    [Fact]
    public async Task SavingChangesAsync_OnInsert_StampsCreatedFieldsOnly()
    {
        // Arrange
        var (context, connection) = NewContext(userId: "user-123");
        using var _ = connection;
        using var __ = context;
        var thing = new AuditableThing { Name = "widget" };

        // Act
        context.Things.Add(thing);
        await context.SaveChangesAsync();

        // Assert
        Assert.Equal(Now, thing.CreatedAt);
        Assert.Equal("user-123", thing.CreatedBy);
        Assert.Null(thing.UpdatedAt);
        Assert.Null(thing.UpdatedBy);
    }

    [Fact]
    public async Task SavingChangesAsync_OnUpdate_StampsUpdatedFields()
    {
        // Arrange
        var (context, connection) = NewContext(userId: "editor-9");
        using var _ = connection;
        using var __ = context;
        var thing = new AuditableThing { Name = "widget" };
        context.Things.Add(thing);
        await context.SaveChangesAsync();

        // Act
        thing.Name = "renamed";
        await context.SaveChangesAsync();

        // Assert
        Assert.Equal(Now, thing.UpdatedAt);
        Assert.Equal("editor-9", thing.UpdatedBy);
    }

    [Fact]
    public async Task SavingChangesAsync_OnUpdate_DoesNotOverwriteCreatedFields()
    {
        // Arrange — insert as creator-1 and capture the original created stamp.
        var (context, connection) = NewContext(userId: "creator-1");
        using var _ = connection;
        using var __ = context;
        var thing = new AuditableThing { Name = "widget" };
        context.Things.Add(thing);
        await context.SaveChangesAsync();
        var originalCreatedAt = thing.CreatedAt;

        // Act — a buggy caller tampers with the created fields during an update.
        thing.Name = "renamed";
        thing.CreatedAt = Now.AddYears(-5);
        thing.CreatedBy = "attacker";
        await context.SaveChangesAsync();
        await context.Entry(thing).ReloadAsync(); // read back what was actually persisted

        // Assert — the tamper was dropped; the original insert stamp survives.
        Assert.Equal(originalCreatedAt, thing.CreatedAt);
        Assert.Equal("creator-1", thing.CreatedBy);
    }

    [Fact]
    public async Task SavingChangesAsync_WhenNoCurrentUser_LeavesCreatedByNull()
    {
        // Arrange — anonymous context (seed data / background worker).
        var (context, connection) = NewContext(userId: null);
        using var _ = connection;
        using var __ = context;
        var thing = new AuditableThing { Name = "seeded" };

        // Act
        context.Things.Add(thing);
        await context.SaveChangesAsync();

        // Assert — timestamp still set, but the actor is null ("system").
        Assert.Equal(Now, thing.CreatedAt);
        Assert.Null(thing.CreatedBy);
    }

    [Fact]
    public void SavingChanges_SyncPath_AlsoStampsCreatedFields()
    {
        // Arrange — proves the synchronous SaveChanges() override runs Stamp too,
        // not just the async path (they are separate interceptor methods).
        var (context, connection) = NewContext(userId: "user-123");
        using var _ = connection;
        using var __ = context;
        var thing = new AuditableThing { Name = "widget" };

        // Act
        context.Things.Add(thing);
        context.SaveChanges();

        // Assert
        Assert.Equal(Now, thing.CreatedAt);
        Assert.Equal("user-123", thing.CreatedBy);
    }
}
