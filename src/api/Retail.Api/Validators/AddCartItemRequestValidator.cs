using FluentValidation;
using Retail.Api.DTOs.Requests;

namespace Retail.Api.Validators;

/// <summary>Validates <see cref="AddCartItemRequest"/>: a real variant id and a sane quantity.</summary>
public sealed class AddCartItemRequestValidator : AbstractValidator<AddCartItemRequest>
{
    public AddCartItemRequestValidator()
    {
        RuleFor(x => x.ProductVariantId).NotEmpty();
        // 1..99 per add. The 99 ceiling is a guard against fat-finger/abuse, not a stock
        // check — real availability is enforced against InventoryItem in the service.
        RuleFor(x => x.Quantity).InclusiveBetween(1, 99);
    }
}
