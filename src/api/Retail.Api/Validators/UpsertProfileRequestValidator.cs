using FluentValidation;
using Retail.Api.DTOs.Requests;

namespace Retail.Api.Validators;

/// <summary>Validates <see cref="UpsertProfileRequest"/> shape (lengths mirror the EF config).</summary>
public sealed class UpsertProfileRequestValidator : AbstractValidator<UpsertProfileRequest>
{
    public UpsertProfileRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(120);
        // Phone is optional; when present, cap the length and allow only the
        // characters a phone number can contain (digits, +, spaces, hyphens, parens).
        RuleFor(x => x.Phone)
            .MaximumLength(32)
            .Matches(@"^\+?[0-9\s\-()]{7,}$")
            .WithMessage("Phone must be a valid phone number.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));
    }
}
