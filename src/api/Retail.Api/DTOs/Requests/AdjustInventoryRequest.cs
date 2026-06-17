namespace Retail.Api.DTOs.Requests;

/// <summary>Admin stock adjustment (Phase 3 §11): a signed <see cref="Delta"/> applied to on-hand, with a reason for the audit trail.</summary>
public sealed record AdjustInventoryRequest(int Delta, string Reason);
