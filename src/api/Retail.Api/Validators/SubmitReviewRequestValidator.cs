using FluentValidation;
using Retail.Api.DTOs.Requests;

namespace Retail.Api.Validators;

/// <summary>Validates <see cref="SubmitReviewRequest"/>: rating 1..5, non-empty body ≤ 4000 chars (mirrors DATABASE_DESIGN §3.15).</summary>
public sealed class SubmitReviewRequestValidator : AbstractValidator<SubmitReviewRequest>
{
    public SubmitReviewRequestValidator()
    {
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(4000);
    }
}
