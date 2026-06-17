using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail.Api.Common.Constants;
using Retail.Api.Common.Models;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Services;

namespace Retail.Api.Controllers;

/// <summary>
/// The admin order workbench (Phase 3 §8) — read the all-orders list/detail. Fulfilment (ship /
/// deliver) and admin refund land alongside these. Reads require the <c>Orders.View</c> policy
/// (Staff + StoreManager + Administrator).
/// </summary>
[ApiController]
[Route("api/v1/admin/orders")]
public sealed class AdminOrdersController : ControllerBase
{
    private readonly IAdminOrderService _orders;

    public AdminOrdersController(IAdminOrderService orders)
    {
        _orders = orders;
    }

    /// <summary>Lists all orders (paged) with optional status / date-range / customer-email filters.</summary>
    [HttpGet]
    [Authorize(Policy = Roles.Policies.OrdersView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AdminOrderSummaryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListOrders([FromQuery] AdminOrderListQuery query, CancellationToken ct)
    {
        PagedResult<AdminOrderSummaryDto> result = await _orders.ListAsync(query, ct);
        return Ok(ApiResponse<PagedResult<AdminOrderSummaryDto>>.Ok(result));
    }

    /// <summary>Full admin detail for one order (payments + shipment).</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = Roles.Policies.OrdersView)]
    [ProducesResponseType(typeof(ApiResponse<AdminOrderDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrder(Guid id, CancellationToken ct)
    {
        AdminOrderDetailDto order = await _orders.GetAsync(id, ct);
        return Ok(ApiResponse<AdminOrderDetailDto>.Ok(order));
    }
}
