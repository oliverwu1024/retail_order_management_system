using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Retail.Api.Common.Constants;
using Retail.Api.Common.Models;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Services;

namespace Retail.Api.Controllers;

/// <summary>
/// Admin inventory operations (Phase 3 §11) — stock adjustments. Requires the <c>Inventory.Adjust</c>
/// policy (Staff + StoreManager + Administrator). Every adjustment is audited.
/// </summary>
[ApiController]
[Route("api/v1/admin/inventory")]
public sealed class AdminInventoryController : ControllerBase
{
    private readonly IAdminInventoryService _inventory;
    private readonly IValidator<AdjustInventoryRequest> _validator;

    public AdminInventoryController(IAdminInventoryService inventory, IValidator<AdjustInventoryRequest> validator)
    {
        _inventory = inventory;
        _validator = validator;
    }

    /// <summary>Applies a signed delta to a variant's on-hand stock (with a reason).</summary>
    [HttpPost("{variantId:guid}/adjust")]
    [Authorize(Policy = Roles.Policies.InventoryAdjust)]
    [ProducesResponseType(typeof(ApiResponse<StockDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Adjust(Guid variantId, [FromBody] AdjustInventoryRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(_validator, request, ct) is { } invalid)
        {
            return invalid;
        }

        StockDto stock = await _inventory.AdjustAsync(variantId, request, ct);
        return Ok(ApiResponse<StockDto>.Ok(stock));
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
