using FluentValidation;
using Retail.Api.DTOs.Requests;

namespace Retail.Api.Validators;

/// <summary>Validates <see cref="UpdateCartItemRequest"/>: an absolute quantity of 1..99 (use DELETE to remove).</summary>
public sealed class UpdateCartItemRequestValidator : AbstractValidator<UpdateCartItemRequest>
{
    public UpdateCartItemRequestValidator()
    {
        RuleFor(x => x.Quantity).InclusiveBetween(1, 99);
    }
}
