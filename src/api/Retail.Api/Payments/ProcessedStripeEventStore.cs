using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Payments;

/// <summary>EF Core implementation of <see cref="IProcessedStripeEventStore"/>.</summary>
public sealed class ProcessedStripeEventStore : IProcessedStripeEventStore
{
    private readonly RetailDbContext _db;

    public ProcessedStripeEventStore(RetailDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<bool> IsProcessedAsync(string stripeEventId, CancellationToken ct) =>
        await _db.ProcessedStripeEvents.AsNoTracking().AnyAsync(e => e.StripeEventId == stripeEventId, ct);

    /// <inheritdoc />
    public async Task RecordAsync(string stripeEventId, string eventType, DateTimeOffset receivedAt, CancellationToken ct)
    {
        var record = new ProcessedStripeEvent
        {
            StripeEventId = stripeEventId,
            EventType = eventType,
            ReceivedAt = receivedAt,
        };
        _db.ProcessedStripeEvents.Add(record);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A concurrent delivery recorded the same event id first (UX_ProcessedStripeEvent_StripeEventId).
            // That's the idempotent outcome — detach our losing insert and move on.
            _db.Entry(record).State = EntityState.Detached;
        }
    }
}
