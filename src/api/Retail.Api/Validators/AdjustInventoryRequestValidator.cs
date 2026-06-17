using FluentValidation;
using Retail.Api.DTOs.Requests;

namespace Retail.Api.Validators;

/// <summary>Validates <see cref="AdjustInventoryRequest"/>: a non-zero delta and a reason (≤ 200) for the audit trail.</summary>
public sealed class AdjustInventoryRequestValidator : AbstractValidator<AdjustInventoryRequest>
{
    public AdjustInventoryRequestValidator()
    {
        RuleFor(x => x.Delta).NotEqual(0).WithMessage("Delta must be non-zero.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(200);
    }
}
