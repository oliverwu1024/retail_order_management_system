using Microsoft.EntityFrameworkCore;
using Retail.Api.Common.Abstractions;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Exceptions;

namespace Retail.Api.Services;

/// <summary>
/// Admin stock adjustments (Phase 3 §11). A TRACKED load + mutate + SaveChanges, so the
/// AuditTrailInterceptor auto-records the InventoryItem change AND <c>InventoryItem.RowVersion</c>
/// makes concurrent adjustments safe (a stale write → 409). A named "InventoryAdjusted" audit row
/// (with the delta + reason) is staged in the same SaveChanges — this is the deliberate audited
/// path for admin stock changes (the set-based reserve/commit/restock system flows are
/// ExecuteUpdate-based and bypass the interceptor by design; see PHASE_3_SCOPE.md §3.2).
/// </summary>
public sealed class AdminInventoryService : IAdminInventoryService
{
    private readonly RetailDbContext _db;
    private readonly IAuditWriter _audit;

    public AdminInventoryService(RetailDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <inheritdoc />
    public async Task<StockDto> AdjustAsync(Guid variantId, AdjustInventoryRequest request, CancellationToken ct)
    {
        InventoryItem item = await _db.InventoryItems.FirstOrDefaultAsync(i => i.ProductVariantId == variantId, ct)
            ?? throw new NotFoundException($"No inventory found for variant '{variantId}'.");

        int before = item.OnHand;
        int after = before + request.Delta;
        // Reject anything that would make AVAILABLE (= OnHand − Reserved) negative, not merely
        // OnHand < 0. Reserved units are already promised to in-flight checkouts; letting on-hand
        // drop below them oversells and breaks the ledger when those reservations commit
        // (CommitReservedAsync subtracts unconditionally). With Reserved = 0 this is the on-hand ≥ 0 guard.
        if (after < item.Reserved)
        {
            throw new ConflictException(
                $"Adjustment would leave on-hand ({after}) below reserved stock ({item.Reserved}), "
                + $"making available negative (current on-hand {before}, reserved {item.Reserved}, delta {request.Delta}).");
        }

        item.OnHand = after;
        _audit.Record(
            "InventoryAdjusted",
            nameof(InventoryItem),
            item.Id.ToString(),
            before: new { OnHand = before },
            after: new { OnHand = after, request.Delta, request.Reason });

        await _db.SaveChangesAsync(ct);

        return new StockDto(variantId, item.OnHand, item.Reserved, item.Available);
    }
}
