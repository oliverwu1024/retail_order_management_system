namespace Retail.Api.DTOs.Responses;

/// <summary>An audit-trail row as shown in the viewer (Phase 3 §7).</summary>
public sealed record AuditLogDto(
    long Id,
    string Actor,
    string Action,
    string EntityType,
    string EntityId,
    string? BeforeJson,
    string? AfterJson,
    DateTimeOffset OccurredAt);
