using FluentValidation;
using Retail.Api.DTOs.Requests;

namespace Retail.Api.Validators;

/// <summary>Validates <see cref="UpdateVariantRequest"/>: non-negative money.</summary>
public sealed class UpdateVariantRequestValidator : AbstractValidator<UpdateVariantRequest>
{
    public UpdateVariantRequestValidator()
    {
        RuleFor(x => x.PriceCents).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CompareAtPriceCents).GreaterThanOrEqualTo(0).When(x => x.CompareAtPriceCents.HasValue);
    }
}
