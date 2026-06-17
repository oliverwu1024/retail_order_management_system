using Retail.Api.Common.Enums;
using Retail.Api.Common.Models;
using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Exceptions;
using Retail.Api.Mappers;
using Retail.Api.Repositories;

namespace Retail.Api.Services;

/// <summary>Admin order workbench (Phase 3 §8). Reads are not owner-scoped (staff see all orders);
/// authorization is enforced at the controller via the Orders.* policies.</summary>
public sealed class AdminOrderService : IAdminOrderService
{
    private readonly IOrderRepository _orders;

    public AdminOrderService(IOrderRepository orders)
    {
        _orders = orders;
    }

    /// <inheritdoc />
    public async Task<PagedResult<AdminOrderSummaryDto>> ListAsync(AdminOrderListQuery query, CancellationToken ct)
    {
        int safePage = query.Page < 1 ? 1 : query.Page;
        int safeSize = Math.Clamp(query.PageSize, 1, 100);

        (IReadOnlyList<Order> items, int total) = await _orders.GetPagedForAdminAsync(
            ParseStatus(query.Status), query.From, query.To, query.CustomerEmail, safePage, safeSize, ct);

        return new PagedResult<AdminOrderSummaryDto>(
            items.Select(order => order.ToAdminSummaryDto()).ToList(), total, safePage, safeSize);
    }

    /// <inheritdoc />
    public async Task<AdminOrderDetailDto> GetAsync(Guid orderId, CancellationToken ct)
    {
        Order order = await _orders.GetDetailForAdminAsync(orderId, ct)
            ?? throw new NotFoundException($"Order '{orderId}' was not found.");
        return order.ToAdminDetailDto();
    }

    // A blank or unrecognised status filter means "all" (lenient — it's a filter, not a command).
    private static OrderStatus? ParseStatus(string? status) =>
        Enum.TryParse(status, ignoreCase: true, out OrderStatus parsed) ? parsed : null;
}
