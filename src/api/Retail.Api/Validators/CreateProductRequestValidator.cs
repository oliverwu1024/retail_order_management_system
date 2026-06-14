using FluentValidation;
using Retail.Api.DTOs.Requests;

namespace Retail.Api.Validators;

/// <summary>Validates <see cref="CreateProductRequest"/> shape. SKU/slug uniqueness + category existence are checked in the service.</summary>
public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.CategoryId).NotEmpty().WithMessage("A category is required.");
        RuleFor(x => x.Slug).MaximumLength(160).When(x => !string.IsNullOrWhiteSpace(x.Slug));
        RuleFor(x => x.SeoTitle).MaximumLength(200).When(x => x.SeoTitle is not null);
        RuleFor(x => x.SeoDescription).MaximumLength(400).When(x => x.SeoDescription is not null);
        RuleFor(x => x.BrandName).MaximumLength(120).When(x => x.BrandName is not null);
    }
}
