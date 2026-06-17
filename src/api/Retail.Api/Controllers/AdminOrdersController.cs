using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail.Api.Common.Constants;
using Retail.Api.Common.Models;
using Retail.Api.Common.Validation;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Services;

namespace Retail.Api.Controllers;

/// <summary>
/// The admin order workbench (Phase 3 §8): read the all-orders list/detail (Orders.View), fulfil
/// orders — mark shipped/delivered (Orders.Fulfill) — and issue an admin refund (Orders.Refund).
/// State-transition rules are enforced in the service (→ 409). The ship + refund status
/// transitions are guarded by <c>Order.RowVersion</c> (a stale write → 409); deliver is an
/// idempotent shipment update (a concurrent double-deliver is benign, last-write-wins).
/// </summary>
[ApiController]
[Route("api/v1/admin/orders")]
public sealed class AdminOrdersController : ControllerBase
{
    private readonly IAdminOrderService _orders;
    private readonly IValidator<MarkShippedRequest> _shipValidator;

    public AdminOrdersController(IAdminOrderService orders, IValidator<MarkShippedRequest> shipValidator)
    {
        _orders = orders;
        _shipValidator = shipValidator;
    }

    /// <summary>Lists all orders (paged) with optional status / date-range / customer-email filters.</summary>
    [HttpGet]
    [Authorize(Policy = Roles.Policies.OrdersView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AdminOrderSummaryDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ListOrders([FromQuery] AdminOrderListQuery query, CancellationToken ct)
    {
        if (DateRangeGuard.Validate(query.From, query.To) is { } invalid)
        {
            return UnprocessableEntity(invalid);
        }

        PagedResult<AdminOrderSummaryDto> result = await _orders.ListAsync(query, ct);
        return Ok(ApiResponse<PagedResult<AdminOrderSummaryDto>>.Ok(result));
    }

    /// <summary>Full admin detail for one order (payments + shipment).</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Policy = Roles.Policies.OrdersView)]
    [ProducesResponseType(typeof(ApiResponse<AdminOrderDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrder(Guid id, CancellationToken ct)
    {
        AdminOrderDetailDto order = await _orders.GetAsync(id, ct);
        return Ok(ApiResponse<AdminOrderDetailDto>.Ok(order));
    }

    /// <summary>Marks a Paid order as shipped (carrier + tracking → a Shipment; the order → Fulfilled).</summary>
    [HttpPost("{id:guid}/ship")]
    [Authorize(Policy = Roles.Policies.OrdersFulfill)]
    [ProducesResponseType(typeof(ApiResponse<AdminOrderDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> MarkShipped(Guid id, [FromBody] MarkShippedRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(_shipValidator, request, ct) is { } invalid)
        {
            return invalid;
        }

        AdminOrderDetailDto order = await _orders.MarkShippedAsync(id, request, ct);
        return Ok(ApiResponse<AdminOrderDetailDto>.Ok(order));
    }

    /// <summary>Marks a shipped order's shipment as delivered.</summary>
    [HttpPost("{id:guid}/deliver")]
    [Authorize(Policy = Roles.Policies.OrdersFulfill)]
    [ProducesResponseType(typeof(ApiResponse<AdminOrderDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkDelivered(Guid id, CancellationToken ct)
    {
        AdminOrderDetailDto order = await _orders.MarkDeliveredAsync(id, ct);
        return Ok(ApiResponse<AdminOrderDetailDto>.Ok(order));
    }

    /// <summary>Issues a full refund for a Paid/Fulfilled order (StoreManager + Administrator).</summary>
    [HttpPost("{id:guid}/refund")]
    [Authorize(Policy = Roles.Policies.OrdersRefund)]
    [ProducesResponseType(typeof(ApiResponse<AdminOrderDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Refund(Guid id, CancellationToken ct)
    {
        AdminOrderDetailDto order = await _orders.RefundAsync(id, ct);
        return Ok(ApiResponse<AdminOrderDetailDto>.Ok(order));
    }

    // Runs the validator; returns a 422 result if invalid, or null to continue.
    private async Task<IActionResult?> ValidateAsync<T>(IValidator<T> validator, T request, CancellationToken ct)
    {
        ValidationResult result = await validator.ValidateAsync(request, ct);
        return result.IsValid
            ? null
            : UnprocessableEntity(ApiResponse.Fail("Validation failed.", ToApiErrors(result)));
    }

    private static IReadOnlyList<ApiError> ToApiErrors(ValidationResult validation) =>
        validation.Errors
            .Select(failure => new ApiError
            {
                Code = "VALIDATION_ERROR",
                Message = failure.ErrorMessage,
                Field = failure.PropertyName,
            })
            .ToList();
}
