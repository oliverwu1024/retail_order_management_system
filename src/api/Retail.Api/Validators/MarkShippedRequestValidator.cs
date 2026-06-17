using FluentValidation;
using Retail.Api.DTOs.Requests;

namespace Retail.Api.Validators;

/// <summary>Validates <see cref="MarkShippedRequest"/>: carrier ≤ 60, tracking number ≤ 120, both required.</summary>
public sealed class MarkShippedRequestValidator : AbstractValidator<MarkShippedRequest>
{
    public MarkShippedRequestValidator()
    {
        RuleFor(x => x.Carrier).NotEmpty().MaximumLength(60);
        RuleFor(x => x.TrackingNumber).NotEmpty().MaximumLength(120);
    }
}
