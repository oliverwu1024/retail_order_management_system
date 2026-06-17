using System.Text.Json;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Common.Abstractions;

/// <summary>
/// Default <see cref="IAuditWriter"/> — stages an <see cref="AuditLog"/> row on the request's
/// <see cref="RetailDbContext"/> so it commits with the caller's unit of work.
/// </summary>
/// <remarks>
/// Scoped, so it shares the same DbContext instance the calling service uses. It only
/// <c>Add</c>s the row — it does NOT call SaveChanges; the business operation's own SaveChanges
/// flushes the audit row inside the same transaction (atomic with the change it describes).
/// Actor comes from <see cref="ICurrentUserAccessor"/> (null → "system") and the timestamp from
/// the injected <see cref="TimeProvider"/> for test determinism.
/// </remarks>
public sealed class AuditWriter : IAuditWriter
{
    private readonly RetailDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly TimeProvider _timeProvider;

    public AuditWriter(RetailDbContext db, ICurrentUserAccessor currentUser, TimeProvider timeProvider)
    {
        _db = db;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public void Record(string action, string entityType, string entityId, object? before = null, object? after = null)
    {
        _db.Set<AuditLog>().Add(new AuditLog
        {
            Actor = _currentUser.UserId ?? "system",
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after),
            OccurredAt = _timeProvider.GetUtcNow(),
        });
    }
}
