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
/// Back-office user administration (Phase 3 §10) — list accounts and create Staff/StoreManager
/// accounts.
/// </summary>
/// <remarks>
/// The whole controller requires the <c>Users.ManageStaff</c> policy (StoreManager + Administrator).
/// Creating a <c>StoreManager</c> additionally requires the Administrator role, checked in the
/// handler — a single policy can't express "Staff may be created by a StoreManager, but a
/// StoreManager only by an Administrator" (REQUIREMENTS §1.3).
/// </remarks>
[ApiController]
[Route("api/v1/admin/users")]
[Authorize(Policy = Roles.Policies.UsersManageStaff)]
public sealed class AdminUsersController : ControllerBase
{
    private readonly IAdminUserService _users;
    private readonly IValidator<CreateUserRequest> _createValidator;

    public AdminUsersController(IAdminUserService users, IValidator<CreateUserRequest> createValidator)
    {
        _users = users;
        _createValidator = createValidator;
    }

    /// <summary>Lists back-office accounts, optionally filtered by role.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AdminUserDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListUsers([FromQuery] AdminUserListQuery query, CancellationToken ct)
    {
        PagedResult<AdminUserDto> result = await _users.ListAsync(query.Role, query.Page, query.PageSize, ct);
        return Ok(ApiResponse<PagedResult<AdminUserDto>>.Ok(result));
    }

    /// <summary>
    /// Creates a Staff or StoreManager account. StoreManager creation is Administrator-only; a
    /// StoreManager attempting to create another StoreManager gets a 403.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<AdminUserDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(_createValidator, request, ct) is { } invalid)
        {
            return invalid;
        }

        // A StoreManager may create Staff, but only an Administrator may create another StoreManager.
        if (request.Role == Roles.StoreManager && !User.IsInRole(Roles.Administrator))
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse.Fail("Only an Administrator can create a StoreManager account."));
        }

        AdminUserDto created = await _users.CreateAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, ApiResponse<AdminUserDto>.Ok(created));
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
