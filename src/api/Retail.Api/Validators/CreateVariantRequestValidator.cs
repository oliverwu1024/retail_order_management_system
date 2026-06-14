using FluentValidation;
using Retail.Api.DTOs.Requests;

namespace Retail.Api.Validators;

/// <summary>Validates <see cref="CreateVariantRequest"/>: non-empty SKU, non-negative money + stock.</summary>
public sealed class CreateVariantRequestValidator : AbstractValidator<CreateVariantRequest>
{
    public CreateVariantRequestValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(64);
        RuleFor(x => x.PriceCents).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CompareAtPriceCents).GreaterThanOrEqualTo(0).When(x => x.CompareAtPriceCents.HasValue);
        RuleFor(x => x.InitialStock).GreaterThanOrEqualTo(0);
    }
}
