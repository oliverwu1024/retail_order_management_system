using FluentValidation;
using Retail.Api.DTOs.Requests;

namespace Retail.Api.Validators;

/// <summary>Validates a chat webhook turn (Phase 5A). Auto-registered by <c>AddValidatorsFromAssemblyContaining</c>.</summary>
public sealed class ChatWebhookRequestValidator : AbstractValidator<ChatWebhookRequest>
{
    private const int MaxMessageLength = 4000;

    public ChatWebhookRequestValidator()
    {
        RuleFor(x => x.ConversationId)
            .NotEmpty()
            .Must(value => Guid.TryParse(value, out _))
            .WithMessage("conversationId must be a GUID.");

        RuleFor(x => x.Message)
            .NotEmpty()
            .MaximumLength(MaxMessageLength);
    }
}
