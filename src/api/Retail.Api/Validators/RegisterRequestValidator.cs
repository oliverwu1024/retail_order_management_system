using FluentValidation;
using Retail.Api.DTOs.Requests;

namespace Retail.Api.Validators;

/// <summary>
/// Validates <see cref="RegisterRequest"/> per REQUIREMENTS §1.1: a valid unique
/// email, a non-empty display name, and a password of ≥12 chars containing at
/// least one letter and one digit.
/// </summary>
/// <remarks>
/// This mirrors the Identity password policy configured in <c>Program.cs</c>, but
/// runs first and returns a structured 422 with per-field messages — friendlier
/// than Identity's pass/fail. (Email UNIQUENESS is enforced by Identity at insert
/// time, not here, to avoid a redundant DB round-trip and a check-then-act race.)
/// </remarks>
public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(256);

        RuleFor(x => x.DisplayName)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(12).WithMessage("Password must be at least 12 characters.")
            .Matches("[A-Za-z]").WithMessage("Password must contain at least one letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.");
    }
}
