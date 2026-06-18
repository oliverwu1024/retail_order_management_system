using FluentValidation;
using Retail.Api.DTOs.Requests;

namespace Retail.Api.Validators;

/// <summary>Validates <see cref="SuggestDescriptionRequest"/>: tone + length must be from the allowed sets.</summary>
public sealed class SuggestDescriptionRequestValidator : AbstractValidator<SuggestDescriptionRequest>
{
    private static readonly string[] Tones = ["playful", "professional", "luxury"];
    private static readonly string[] Lengths = ["short", "medium", "long"];

    public SuggestDescriptionRequestValidator()
    {
        RuleFor(x => x.Tone).Must(t => Tones.Contains(t))
            .WithMessage("Tone must be one of: playful, professional, luxury.");
        RuleFor(x => x.Length).Must(l => Lengths.Contains(l))
            .WithMessage("Length must be one of: short, medium, long.");
    }
}
