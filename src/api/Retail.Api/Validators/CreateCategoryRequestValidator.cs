using FluentValidation;
using Retail.Api.DTOs.Requests;

namespace Retail.Api.Validators;

/// <summary>Validates <see cref="CreateCategoryRequest"/>. Slug uniqueness + parent depth are checked in the service.</summary>
public sealed class CreateCategoryRequestValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(140);
        RuleFor(x => x.Slug).MaximumLength(140).When(x => !string.IsNullOrWhiteSpace(x.Slug));
    }
}
