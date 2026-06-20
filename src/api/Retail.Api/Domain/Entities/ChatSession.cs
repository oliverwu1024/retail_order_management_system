using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// One customer support-chat conversation thread (DATABASE_DESIGN §3.20) — Phase 5A.
/// </summary>
/// <remarks>
/// <para>
/// UPSERTED BY <see cref="ConversationId"/>. The storefront chat widget generates a
/// GUID per drawer session and sends it with every turn; the backend creates the
/// session on the first turn and reuses it thereafter. <see cref="ConversationId"/>
/// is stored as <c>char(36)</c> (a GUID-as-string) rather than a <see cref="Guid"/>
/// because the same contract must later accept a Copilot Studio conversation id
/// (Phase 6), and it carries a UNIQUE index so the upsert has a single key.
/// </para>
/// <para>
/// <see cref="CustomerProfileId"/> IS NULLABLE only to leave that anonymous/Copilot
/// door open — in 5A the chat webhook is <c>[Authorize(Roles = Customer)]</c>, so a
/// session is ALWAYS owned by a logged-in customer and the value is always set.
/// </para>
/// <para>
/// APPEND-ONLY. Chat history is a conversational log: there is no soft-delete flag
/// and no global query filter, and <c>ChatSession</c>/<c>ChatMessage</c> are
/// deliberately kept OFF the <c>AuditTrailInterceptor</c> allowlist (high volume,
/// low forensic value — same call as <c>Review</c>). The <see cref="IAuditableEntity"/>
/// column stamps still apply via <c>AuditingInterceptor</c>; the <c>start_return</c>
/// refund a chat may trigger is audited separately by <c>OrderRefundService</c>.
/// </para>
/// </remarks>
public class ChatSession : IAuditableEntity
{
    /// <summary>Surrogate PK (client-generated GUID — no DB default, per the project convention).</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// FK to the owning <see cref="Entities.CustomerProfile"/>. Nullable for a future
    /// anonymous/Copilot path; always set in 5A (the webhook requires the Customer role).
    /// </summary>
    public Guid? CustomerProfileId { get; set; }

    /// <summary>Navigation to the owner's profile (null only on a future anonymous session).</summary>
    public CustomerProfile? CustomerProfile { get; set; }

    /// <summary>
    /// The client-supplied conversation id (a GUID string) — the UNIQUE upsert key for the
    /// session. <c>char(36)</c> in SQL; accepts a Copilot Studio id later (Phase 6).
    /// </summary>
    public string ConversationId { get; set; } = string.Empty;

    /// <summary>When the conversation began (UTC).</summary>
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>When the most recent turn was added (UTC) — bumped on every turn for recency ordering.</summary>
    public DateTimeOffset LastMessageAt { get; set; }

    /// <summary>The turns in this conversation, oldest-first.</summary>
    public ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();

    // ── IAuditableEntity (stamped by AuditingInterceptor) ────────────────────
    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }
    /// <inheritdoc />
    public string? CreatedBy { get; set; }
    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }
    /// <inheritdoc />
    public string? UpdatedBy { get; set; }
}
