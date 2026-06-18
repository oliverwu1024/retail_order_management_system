using Retail.Api.Common.Enums;
using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// A customer's product review (DATABASE_DESIGN §3.15) — Phase 4.
/// </summary>
/// <remarks>
/// <para>
/// MEMBER-ONLY, ONE PER PRODUCT. A review is always written by a logged-in
/// <see cref="CustomerProfile"/> (the FK is NON-nullable — there is no guest
/// review path, unlike guest checkout). The unique filtered index
/// <c>UX_Review_ProductId_CustomerProfileId</c> enforces at most one live review
/// per customer per product; the service layer additionally checks the reviewer
/// actually purchased the product before allowing the insert (REQUIREMENTS §6.1).
/// </para>
/// <para>
/// SENTIMENT IS FILLED IN ASYNCHRONOUSLY. On insert, <see cref="SentimentScore"/>,
/// <see cref="SentimentLabel"/> and <see cref="ProcessedAt"/> are all null — the
/// review is "unscored". A background service (<c>ReviewSentimentHostedService</c>,
/// Phase-4 Chunk 3) consumes a <c>ReviewCreated</c> signal, calls Azure AI Language,
/// and writes the three columns back. <see cref="ProcessedAt"/> being null is the
/// "not yet scored / retry me" marker the slow-scan fallback keys off.
/// </para>
/// <para>
/// NOT AUDIT-MONITORED. Unlike Product/Order/Payment/etc., reviews are deliberately
/// kept OFF the <c>AuditTrailInterceptor</c> allowlist (high volume, low forensic
/// value — CODING_STANDARDS). The <see cref="IAuditableEntity"/> column stamps
/// (CreatedAt/By, UpdatedAt/By) still apply via <c>AuditingInterceptor</c>.
/// </para>
/// </remarks>
public class Review : IAuditableEntity
{
    /// <summary>Surrogate PK (DB-generated sequential GUID).</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the reviewed <see cref="Product"/>. Required.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Navigation to the reviewed product.</summary>
    public Product Product { get; set; } = null!;

    /// <summary>FK to the authoring <see cref="CustomerProfile"/>. Required — reviews are member-only.</summary>
    public Guid CustomerProfileId { get; set; }

    /// <summary>Navigation to the author's profile.</summary>
    public CustomerProfile CustomerProfile { get; set; } = null!;

    /// <summary>Star rating 1–5 (tinyint, DB CHECK enforces the range).</summary>
    public byte Rating { get; set; }

    /// <summary>Free-text review body (≤ 4000 chars).</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// Azure AI Language sentiment score = (PositiveScore − NegativeScore), in −1..1.
    /// Null until the background scorer has run. <c>decimal(4,3)</c> in SQL.
    /// </summary>
    public decimal? SentimentScore { get; set; }

    /// <summary>
    /// Azure AI Language overall label (Positive / Neutral / Negative / Mixed).
    /// Null until scored.
    /// </summary>
    public SentimentLabel? SentimentLabel { get; set; }

    /// <summary>
    /// When the AI sentiment scorer last ran for this review (UTC). Null = unscored;
    /// the slow-cycle sweep re-picks rows where this is still null.
    /// </summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Soft-delete flag — hidden by the global query filter when true.</summary>
    public bool IsDeleted { get; set; }

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
