using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Retail.Api.Common.Abstractions;
using Retail.Api.Common.Constants;
using Retail.Api.Common.Models;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Identity;
using Retail.Api.Services;

namespace Retail.Api.Controllers;

/// <summary>
/// Shopping cart endpoints (<c>/api/v1/cart</c>, Story 2.1). Open to everyone: a logged-in
/// customer's cart is keyed to their profile, while a guest's cart is keyed by an HttpOnly
/// <c>anon_cart_key</c> cookie this controller manages. State-changing calls are still
/// CSRF-protected by the global middleware (the SPA fetches a CSRF token for guests too).
/// </summary>
[ApiController]
[Route("api/v1/cart")]
[Produces("application/json")]
[AllowAnonymous]
public sealed class CartController : ControllerBase
{
    private readonly ICartService _carts;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IValidator<AddCartItemRequest> _addValidator;
    private readonly IValidator<UpdateCartItemRequest> _updateValidator;
    private readonly bool _secureCookies;

    public CartController(
        ICartService carts,
        ICurrentUserAccessor currentUser,
        IValidator<AddCartItemRequest> addValidator,
        IValidator<UpdateCartItemRequest> updateValidator,
        IOptions<AuthSettings> authSettings)
    {
        _carts = carts;
        _currentUser = currentUser;
        _addValidator = addValidator;
        _updateValidator = updateValidator;
        _secureCookies = authSettings.Value.SecureCookies;
    }

    /// <summary>Returns the caller's current cart (an empty cart if they have none yet).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<CartDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCart(CancellationToken ct)
    {
        CartResult result = await _carts.GetCartAsync(BuildCaller(), ct);
        return CartResponse(result);
    }

    /// <summary>Adds a variant to the cart (or bumps its quantity). 404 if it isn't sellable.</summary>
    [HttpPost("items")]
    [ProducesResponseType(typeof(ApiResponse<CartDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AddItem([FromBody] AddCartItemRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(_addValidator, request, ct) is { } invalid)
        {
            return invalid;
        }

        CartResult result = await _carts.AddItemAsync(BuildCaller(), request, ct);
        return CartResponse(result);
    }

    /// <summary>Sets the absolute quantity of a line. 404 if the variant isn't in the cart.</summary>
    [HttpPut("items/{variantId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<CartDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateItem(Guid variantId, [FromBody] UpdateCartItemRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(_updateValidator, request, ct) is { } invalid)
        {
            return invalid;
        }

        CartResult result = await _carts.UpdateItemAsync(BuildCaller(), variantId, request, ct);
        return CartResponse(result);
    }

    /// <summary>Removes a line (idempotent — removing a line that isn't there still returns the cart).</summary>
    [HttpDelete("items/{variantId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<CartDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> RemoveItem(Guid variantId, CancellationToken ct)
    {
        CartResult result = await _carts.RemoveItemAsync(BuildCaller(), variantId, ct);
        return CartResponse(result);
    }

    /// <summary>Empties the cart.</summary>
    [HttpDelete]
    [ProducesResponseType(typeof(ApiResponse<CartDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        CartResult result = await _carts.ClearAsync(BuildCaller(), ct);
        return CartResponse(result);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    // Identity for the cart: the auth cookie's user id (a member) if present, plus any guest
    // cart-key cookie. The service decides which wins and whether a login-merge is due.
    private CartCaller BuildCaller() =>
        new(_currentUser.UserId, Request.Cookies[CartConstants.AnonymousCartKeyCookie]);

    // Applies the service's cookie instruction, then returns the cart envelope.
    private IActionResult CartResponse(CartResult result)
    {
        ApplyAnonymousCookie(result.AnonymousKey);
        return Ok(ApiResponse<CartDto>.Ok(result.Cart));
    }

    // A non-null key → (re)write the guest cookie; null → delete it (the caller is a member,
    // or has no cart). HttpOnly so JS can't touch it; SameSite=Lax so it survives the
    // top-level navigation back from Stripe's hosted checkout (Strict cookies wouldn't be sent).
    private void ApplyAnonymousCookie(string? anonymousKey)
    {
        if (!string.IsNullOrEmpty(anonymousKey))
        {
            Response.Cookies.Append(
                CartConstants.AnonymousCartKeyCookie,
                anonymousKey,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = _secureCookies,
                    SameSite = SameSiteMode.Lax,
                    Path = "/",
                    IsEssential = true,
                });
        }
        else
        {
            // The delete options must match the write (path/samesite/secure) or the browser
            // won't treat it as the same cookie and won't remove it.
            Response.Cookies.Delete(
                CartConstants.AnonymousCartKeyCookie,
                new CookieOptions
                {
                    Secure = _secureCookies,
                    SameSite = SameSiteMode.Lax,
                    Path = "/",
                });
        }
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
