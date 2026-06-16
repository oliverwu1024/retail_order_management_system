using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail.Api.Common.Abstractions;
using Retail.Api.Common.Constants;
using Retail.Api.Common.Models;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Services;

namespace Retail.Api.Controllers;

/// <summary>
/// Order + checkout endpoints (<c>/api/v1/orders</c>, Story 2.2). Checkout is open to everyone
/// (guests too); customer order viewing + guest lookup arrive in Chunk 4.
/// </summary>
[ApiController]
[Route("api/v1/orders")]
[Produces("application/json")]
public sealed class OrdersController : ControllerBase
{
    private readonly ICheckoutService _checkout;
    private readonly IOrderQueryService _orders;
    private readonly IOrderCancellationService _cancellation;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IValidator<StartCheckoutRequest> _startCheckoutValidator;

    public OrdersController(
        ICheckoutService checkout,
        IOrderQueryService orders,
        IOrderCancellationService cancellation,
        ICurrentUserAccessor currentUser,
        IValidator<StartCheckoutRequest> startCheckoutValidator)
    {
        _checkout = checkout;
        _orders = orders;
        _cancellation = cancellation;
        _currentUser = currentUser;
        _startCheckoutValidator = startCheckoutValidator;
    }

    /// <summary>Reserves the caller's cart and creates a Stripe Checkout Session (returns the redirect URL).</summary>
    [HttpPost("checkout-session")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CheckoutSessionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateCheckoutSession([FromBody] StartCheckoutRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(_startCheckoutValidator, request, ct) is { } invalid)
        {
            return invalid;
        }

        // The cart is identified by the caller (auth cookie + anon-cart cookie), never the body.
        var caller = new CartCaller(_currentUser.UserId, Request.Cookies[CartConstants.AnonymousCartKeyCookie]);
        CheckoutSessionResponse result = await _checkout.StartCheckoutAsync(caller, request, ct);
        return Ok(ApiResponse<CheckoutSessionResponse>.Ok(result));
    }

    /// <summary>The current customer's orders, newest first (paged).</summary>
    [HttpGet]
    [Authorize(Roles = Roles.Customer)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<OrderSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMyOrders([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        if (!TryGetUserId(out string userId))
        {
            return Unauthorized(ApiResponse.Fail("Not authenticated."));
        }

        PagedResult<OrderSummaryDto> result = await _orders.GetMyOrdersAsync(userId, page, pageSize, ct);
        return Ok(ApiResponse<PagedResult<OrderSummaryDto>>.Ok(result));
    }

    /// <summary>One of the current customer's orders (404 if it isn't theirs).</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = Roles.Customer)]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyOrder(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out string userId))
        {
            return Unauthorized(ApiResponse.Fail("Not authenticated."));
        }

        OrderDetailDto order = await _orders.GetMyOrderAsync(userId, id, ct);
        return Ok(ApiResponse<OrderDetailDto>.Ok(order));
    }

    /// <summary>
    /// Guest order lookup by Stripe session id — the high-entropy bearer the success page holds.
    /// Open to anyone with the session id; the id itself is the (unguessable) access token.
    /// </summary>
    [HttpGet("by-session/{sessionId}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrderBySession(string sessionId, CancellationToken ct)
    {
        OrderDetailDto order = await _orders.GetOrderBySessionAsync(sessionId, ct);
        return Ok(ApiResponse<OrderDetailDto>.Ok(order));
    }

    /// <summary>Cancels one of the current customer's paid orders (refund + restock).</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = Roles.Customer)]
    [ProducesResponseType(typeof(ApiResponse<OrderDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelMyOrder(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out string userId))
        {
            return Unauthorized(ApiResponse.Fail("Not authenticated."));
        }

        OrderDetailDto order = await _cancellation.CancelMyOrderAsync(userId, id, ct);
        return Ok(ApiResponse<OrderDetailDto>.Ok(order));
    }

    // [Authorize] guarantees an authenticated principal, but we resolve the id defensively.
    private bool TryGetUserId(out string userId)
    {
        userId = _currentUser.UserId ?? string.Empty;
        return userId.Length > 0;
    }

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
