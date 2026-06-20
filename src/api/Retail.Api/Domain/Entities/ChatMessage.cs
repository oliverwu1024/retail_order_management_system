using Retail.Api.Common.Enums;
using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// A single turn within a <see cref="ChatSession"/> (DATABASE_DESIGN §3.21) — Phase 5A.
/// </summary>
/// <remarks>
/// <para>
/// A turn is one of: a customer message (<see cref="ChatMessageRole.User"/>), an
/// assistant reply (<see cref="ChatMessageRole.Assistant"/>), an optional persisted
/// system/context turn (<see cref="ChatMessageRole.System"/>), or a recorded tool
/// call/result (<see cref="ChatMessageRole.Tool"/>, with <see cref="ToolName"/> +
/// <see cref="ToolPayloadJson"/> populated). See the note on <see cref="ChatMessageRole"/>:
/// <c>Role</c> is a persistence/diagnostics label, NOT an Anthropic wire role.
/// </para>
/// <para>
/// CHILD OF <see cref="ChatSession"/> with <c>Cascade</c> delete (a message cannot
/// outlive its session). Read newest-within-session via the
/// <c>IX_ChatMessage_ChatSessionId_CreatedAt</c> index (CreatedAt from
/// <see cref="IAuditableEntity"/>). Append-only — no soft-delete.
/// </para>
/// </remarks>
public class ChatMessage : IAuditableEntity
{
    /// <summary>Surrogate PK (client-generated GUID — no DB default).</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the parent <see cref="ChatSession"/>. Required.</summary>
    public Guid ChatSessionId { get; set; }

    /// <summary>Navigation to the parent session.</summary>
    public ChatSession ChatSession { get; set; } = null!;

    /// <summary>Who/what authored this turn (tinyint). See <see cref="ChatMessageRole"/>.</summary>
    public ChatMessageRole Role { get; set; }

    /// <summary>The message text (assistant/user/system) or a human-readable tool summary. <c>nvarchar(max)</c>.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>For a <see cref="ChatMessageRole.Tool"/> row, the tool that was called (≤ 80 chars). Null otherwise.</summary>
    public string? ToolName { get; set; }

    /// <summary>For a <see cref="ChatMessageRole.Tool"/> row, the tool arguments or result as JSON. Null otherwise. <c>nvarchar(max)</c>.</summary>
    public string? ToolPayloadJson { get; set; }

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
