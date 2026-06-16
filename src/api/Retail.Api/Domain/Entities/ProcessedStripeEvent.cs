namespace Retail.Api.Domain.Entities;

/// <summary>
/// Idempotency ledger for Stripe webhook events (DATABASE_DESIGN §3.22).
/// </summary>
/// <remarks>
/// <para>
/// Stripe delivers webhooks <em>at least once</em> — the same event can arrive multiple
/// times (retries, network hiccups). Before processing an event the handler inserts its id
/// here under a UNIQUE index; a duplicate-key violation means "already handled" → the
/// handler short-circuits. This is what makes order creation safe against redelivery.
/// </para>
/// <para>
/// A TECHNICAL, append-only table — deliberately NOT <c>IAuditableEntity</c> (it has no
/// "who/when updated" story; it's write-once) and it uses a <c>bigint</c> IDENTITY PK
/// rather than the Guid surrogate the domain entities use. For a high-volume, ever-growing
/// log, a narrow monotonic clustered key avoids the page-split / fragmentation cost a
/// random GUID clustered key would incur. Old rows are pruned on a retention schedule.
/// </para>
/// </remarks>
public class ProcessedStripeEvent
{
    /// <summary>Auto-increment PK (<c>bigint</c> identity) — a narrow clustered key for an append-only log.</summary>
    public long Id { get; set; }

    /// <summary>Stripe's event id (<c>evt_...</c>). UNIQUE — this is the idempotency key.</summary>
    public string StripeEventId { get; set; } = string.Empty;

    /// <summary>Stripe event type (e.g. <c>checkout.session.completed</c>).</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>When we recorded the event (UTC). Set by the handler at insert time.</summary>
    public DateTimeOffset ReceivedAt { get; set; }
}
