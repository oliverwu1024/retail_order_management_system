using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>Admin stock adjustments (Phase 3 §11).</summary>
public interface IAdminInventoryService
{
    /// <summary>Applies a signed delta to a variant's on-hand stock (audited). 404 unknown variant; 409 if it would go negative.</summary>
    Task<StockDto> AdjustAsync(Guid variantId, AdjustInventoryRequest request, CancellationToken ct);
}
