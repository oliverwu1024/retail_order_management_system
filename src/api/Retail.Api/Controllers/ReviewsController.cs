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
/// Product reviews (Phase 4, Story 4.1). Submitting is for the verified-purchase customer;
/// reading is public. Both hang off a product (<c>/api/v1/products/{productId}/reviews</c>).
/// </summary>
[ApiController]
[Route("api/v1/products/{productId:guid}/reviews")]
[Produces("application/json")]
public sealed class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviews;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IValidator<SubmitReviewRequest> _submitValidator;

    public ReviewsController(
        IReviewService reviews,
        ICurrentUserAccessor currentUser,
        IValidator<SubmitReviewRequest> submitValidator)
    {
        _reviews = reviews;
        _currentUser = currentUser;
        _submitValidator = submitValidator;
    }

    /// <summary>Submits a review for a product the caller has purchased.</summary>
    [HttpPost]
    [Authorize(Roles = Roles.Customer)]
    [ProducesResponseType(typeof(ApiResponse<ReviewDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Submit(Guid productId, [FromBody] SubmitReviewRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(_submitValidator, request, ct) is { } invalid)
        {
            return invalid;
        }

        if (!TryGetUserId(out string userId))
        {
            return Unauthorized(ApiResponse.Fail("Not authenticated."));
        }

        ReviewDto review = await _reviews.SubmitReviewAsync(userId, productId, request, ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<ReviewDto>.Ok(review));
    }

    /// <summary>Lists a product's reviews (paged) plus the average + rating distribution. Public.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<ReviewListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid productId, [FromQuery] ReviewListQuery query, CancellationToken ct)
    {
        ReviewListDto result = await _reviews.ListReviewsAsync(productId, query.Page, query.PageSize, ct);
        return Ok(ApiResponse<ReviewListDto>.Ok(result));
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
