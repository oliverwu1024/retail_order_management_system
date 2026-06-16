using FluentValidation;
using Retail.Api.DTOs.Requests;

namespace Retail.Api.Validators;

/// <summary>Validates a gallery image edit (PRODUCT_IMAGES_SCOPE) — alt text is bounded to the column length.</summary>
public sealed class UpdateProductImageRequestValidator : AbstractValidator<UpdateProductImageRequest>
{
    public UpdateProductImageRequestValidator()
    {
        RuleFor(x => x.AltText).MaximumLength(200).When(x => x.AltText is not null);
    }
}
