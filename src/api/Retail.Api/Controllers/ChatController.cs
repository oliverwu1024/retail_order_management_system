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
/// Customer support chatbot (Phase 5A). The storefront drawer posts one turn at a time to
/// <c>/api/v1/chat/webhook</c>. Despite the name it is a BROWSER-called endpoint, so it requires the
/// customer's auth cookie + CSRF (NOT anonymous, NOT CSRF-exempt like the Stripe webhook). An AI
/// outage degrades to a friendly 200 reply inside <see cref="IChatService"/>, never a 5xx.
/// </summary>
[ApiController]
[Route("api/v1/chat")]
[Produces("application/json")]
public sealed class ChatController : ControllerBase
{
    private readonly IChatService _chat;
    private readonly IChatQueryService _chatQuery;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IValidator<ChatWebhookRequest> _validator;

    public ChatController(
        IChatService chat,
        IChatQueryService chatQuery,
        ICurrentUserAccessor currentUser,
        IValidator<ChatWebhookRequest> validator)
    {
        _chat = chat;
        _chatQuery = chatQuery;
        _currentUser = currentUser;
        _validator = validator;
    }

    /// <summary>Handles one customer chat turn and returns the assistant's reply.</summary>
    [HttpPost("webhook")]
    [Authorize(Roles = Roles.Customer)]
    [ProducesResponseType(typeof(ApiResponse<ChatTurnDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Webhook([FromBody] ChatWebhookRequest request, CancellationToken ct)
    {
        if (await ValidateAsync(_validator, request, ct) is { } invalid)
        {
            return invalid;
        }

        if (!TryGetUserId(out string userId))
        {
            return Unauthorized(ApiResponse.Fail("Not authenticated."));
        }

        ChatTurnDto turn = await _chat.HandleTurnAsync(userId, request, ct);
        return Ok(ApiResponse<ChatTurnDto>.Ok(turn));
    }

    /// <summary>Admin diagnostics: lists chat sessions (most recently active first), paged.</summary>
    [HttpGet("sessions")]
    [Authorize(Policy = Roles.Policies.ChatView)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ChatSessionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListSessions([FromQuery] ChatSessionListQuery query, CancellationToken ct)
    {
        PagedResult<ChatSessionDto> result = await _chatQuery.ListSessionsAsync(query, ct);
        return Ok(ApiResponse<PagedResult<ChatSessionDto>>.Ok(result));
    }

    /// <summary>Admin diagnostics: one chat session with its full message history.</summary>
    [HttpGet("sessions/{id:guid}")]
    [Authorize(Policy = Roles.Policies.ChatView)]
    [ProducesResponseType(typeof(ApiResponse<ChatSessionDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSession(Guid id, CancellationToken ct)
    {
        ChatSessionDetailDto session = await _chatQuery.GetSessionAsync(id, ct);
        return Ok(ApiResponse<ChatSessionDetailDto>.Ok(session));
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
