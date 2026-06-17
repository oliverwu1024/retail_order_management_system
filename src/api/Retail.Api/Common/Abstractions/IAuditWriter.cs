namespace Retail.Api.Common.Abstractions;

/// <summary>
/// Writes an explicit, human-meaningful business-action row to the audit trail — the
/// companion to the automatic CRUD capture in <c>AuditTrailInterceptor</c>.
/// </summary>
/// <remarks>
/// The interceptor records generic Insert/Update/Delete rows; it cannot name the intent
/// ("Refund" vs a plain Status change). Services that perform a notable operation (refund,
/// ship, inventory-adjust) call <see cref="Record"/> so the viewer shows a legible event.
/// The row is added to the CURRENT <c>RetailDbContext</c>, so it commits atomically with the
/// business change in the caller's SaveChanges (a rolled-back change rolls back its audit row).
/// </remarks>
public interface IAuditWriter
{
    /// <summary>
    /// Stages an audit row (Actor and timestamp are resolved internally). <paramref name="before"/>
    /// and <paramref name="after"/> are serialized to JSON — pass small anonymous objects of the
    /// fields that matter (never raw PII).
    /// </summary>
    void Record(string action, string entityType, string entityId, object? before = null, object? after = null);
}
