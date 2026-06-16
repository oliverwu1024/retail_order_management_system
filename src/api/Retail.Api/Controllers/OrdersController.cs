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
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IValidator<StartCheckoutRequest> _startCheckoutValidator;

    public OrdersController(
        ICheckoutService checkout,
        ICurrentUserAccessor currentUser,
        IValidator<StartCheckoutRequest> startCheckoutValidator)
    {
        _checkout = checkout;
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
