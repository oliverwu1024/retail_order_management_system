using FluentValidation;
using Retail.Api.DTOs.Requests;

namespace Retail.Api.Validators;

/// <summary>
/// Validates <see cref="LoginRequest"/> shape only — presence of a well-formed
/// email and a non-empty password. Whether the credentials are CORRECT is the
/// service's job; this just rejects obviously-malformed input before any DB work.
/// </summary>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty();
    }
}
