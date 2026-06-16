namespace Retail.Api.Exceptions;

/// <summary>
/// Thrown when an optimistic-concurrency-guarded write affects 0 rows because another
/// transaction changed the row first (its <c>RowVersion</c> no longer matches the one we read).
/// Mapped to HTTP 409 <c>CONCURRENCY_CONFLICT</c> by <c>ExceptionMiddleware</c>.
/// </summary>
/// <remarks>
/// Distinct from EF Core's <c>DbUpdateConcurrencyException</c> (which a tracked
/// <c>SaveChanges</c> raises automatically): this is for the set-based <c>ExecuteUpdate</c>
/// path, where EF does NOT throw — we inspect the affected-row count ourselves and raise this
/// when it's 0. Both map to the same 409 code so the client handles them identically.
/// </remarks>
public sealed class ConcurrencyException : Exception
{
    public ConcurrencyException(string message)
        : base(message)
    {
    }
}
