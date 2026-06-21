namespace Retail.Api.DTOs.Responses;

/// <summary>A flagged order as shown in the order-anomaly Risk Queue (Phase 5B §7).</summary>
public sealed record AnomalyDto(
    Guid Id,
    Guid OrderId,
    int OrderNumber,
    decimal Score,
    string Reason,
    DateTimeOffset DetectedAt,
    bool Acknowledged);
