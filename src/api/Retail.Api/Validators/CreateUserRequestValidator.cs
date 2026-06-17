using FluentValidation;
using Retail.Api.Common.Constants;
using Retail.Api.DTOs.Requests;

namespace Retail.Api.Validators;

/// <summary>
/// Validates <see cref="CreateUserRequest"/>: a valid email, a display name, the same ≥12-char
/// (letter + digit) password policy as self-registration, and a role restricted to the two
/// back-office roles an admin may mint — <c>Staff</c> or <c>StoreManager</c> (never Customer or
/// Administrator, which would be a privilege-escalation surface).
/// </summary>
public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
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

        RuleFor(x => x.Role)
            .Must(r => r == Roles.Staff || r == Roles.StoreManager)
            .WithMessage($"Role must be '{Roles.Staff}' or '{Roles.StoreManager}'.");
    }
}
