using FluentValidation;
using Retail.Api.DTOs.Requests;

namespace Retail.Api.Validators;

/// <summary>Validates <see cref="AddressRequest"/> shape (lengths mirror the EF config).</summary>
public sealed class AddressRequestValidator : AbstractValidator<AddressRequest>
{
    public AddressRequestValidator()
    {
        RuleFor(x => x.Line1).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Line2).MaximumLength(200).When(x => x.Line2 is not null);
        RuleFor(x => x.City).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Region).MaximumLength(120).When(x => x.Region is not null);
        RuleFor(x => x.PostalCode).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Country)
            .NotEmpty()
            .Matches("^[A-Za-z]{2}$")
            .WithMessage("Country must be an ISO-3166 alpha-2 code (e.g. AU, US).");
    }
}
