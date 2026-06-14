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
/// Customer self-service profile + address endpoints (<c>/api/v1/profile</c>, Story 1.4).
/// Every action operates on the signed-in customer's OWN data — the user id comes from the
/// auth cookie (never the route/body), so there's no way to address another user's profile.
/// Restricted to the <c>Customer</c> role: staff/admin accounts have no customer profile.
/// </summary>
[ApiController]
[Route("api/v1/profile")]
[Produces("application/json")]
[Authorize(Roles = Roles.Customer)]
public sealed class CustomerProfileController : ControllerBase
{
    private readonly ICustomerProfileService _profiles;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IValidator<UpsertProfileRequest> _upsertProfileValidator;
    private readonly IValidator<AddressRequest> _addressValidator;

    public CustomerProfileController(
        ICustomerProfileService profiles,
        ICurrentUserAccessor currentUser,
        IValidator<UpsertProfileRequest> upsertProfileValidator,
        IValidator<AddressRequest> addressValidator)
    {
        _profiles = profiles;
        _currentUser = currentUser;
        _upsertProfileValidator = upsertProfileValidator;
        _addressValidator = addressValidator;
    }

    // ── Profile ──────────────────────────────────────────────────────────────────

    /// <summary>Returns the current customer's profile (lazily created on first access).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<CustomerProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyProfile(CancellationToken ct)
    {
        if (!TryGetUserId(out string userId))
        {
            return Unauthorized(ApiResponse.Fail("Not authenticated."));
        }

        CustomerProfileDto profile = await _profiles.GetMyProfileAsync(userId, ct);
        return Ok(ApiResponse<CustomerProfileDto>.Ok(profile));
    }

    /// <summary>Updates the current customer's DisplayName + Phone (Email is immutable).</summary>
    [HttpPut]
    [ProducesResponseType(typeof(ApiResponse<CustomerProfileDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateMyProfile([FromBody] UpsertProfileRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out string userId))
        {
            return Unauthorized(ApiResponse.Fail("Not authenticated."));
        }

        if (await ValidateAsync(_upsertProfileValidator, request, ct) is { } invalid)
        {
            return invalid;
        }

        CustomerProfileDto profile = await _profiles.UpdateMyProfileAsync(userId, request, ct);
        return Ok(ApiResponse<CustomerProfileDto>.Ok(profile));
    }

    // ── Addresses ────────────────────────────────────────────────────────────────

    /// <summary>Lists the current customer's saved addresses (defaults first).</summary>
    [HttpGet("addresses")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AddressDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMyAddresses(CancellationToken ct)
    {
        if (!TryGetUserId(out string userId))
        {
            return Unauthorized(ApiResponse.Fail("Not authenticated."));
        }

        IReadOnlyList<AddressDto> addresses = await _profiles.ListMyAddressesAsync(userId, ct);
        return Ok(ApiResponse<IReadOnlyList<AddressDto>>.Ok(addresses));
    }

    /// <summary>Adds an address. Marking it default unsets the prior default for that axis.</summary>
    [HttpPost("addresses")]
    [ProducesResponseType(typeof(ApiResponse<AddressDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AddAddress([FromBody] AddressRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out string userId))
        {
            return Unauthorized(ApiResponse.Fail("Not authenticated."));
        }

        if (await ValidateAsync(_addressValidator, request, ct) is { } invalid)
        {
            return invalid;
        }

        AddressDto address = await _profiles.AddAddressAsync(userId, request, ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<AddressDto>.Ok(address));
    }

    /// <summary>Updates an address the caller owns (404 otherwise).</summary>
    [HttpPut("addresses/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<AddressDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateAddress(Guid id, [FromBody] AddressRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out string userId))
        {
            return Unauthorized(ApiResponse.Fail("Not authenticated."));
        }

        if (await ValidateAsync(_addressValidator, request, ct) is { } invalid)
        {
            return invalid;
        }

        AddressDto address = await _profiles.UpdateAddressAsync(userId, id, request, ct);
        return Ok(ApiResponse<AddressDto>.Ok(address));
    }

    /// <summary>Deletes an address the caller owns (404 otherwise).</summary>
    [HttpDelete("addresses/{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAddress(Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out string userId))
        {
            return Unauthorized(ApiResponse.Fail("Not authenticated."));
        }

        await _profiles.DeleteAddressAsync(userId, id, ct);
        return Ok(ApiResponse.Ok("Address deleted."));
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    // [Authorize] guarantees an authenticated principal, but we still resolve the id
    // defensively — a token without the NameIdentifier claim shouldn't 500.
    private bool TryGetUserId(out string userId)
    {
        userId = _currentUser.UserId ?? string.Empty;
        return userId.Length > 0;
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
