namespace Retail.Api.DTOs.Responses;

/// <summary>A variant's stock levels after an adjustment (Phase 3 §11). Available = OnHand − Reserved.</summary>
public sealed record StockDto(Guid ProductVariantId, int OnHand, int Reserved, int Available);
