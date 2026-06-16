using FluentValidation;
using Retail.Api.DTOs.Requests;

namespace Retail.Api.Validators;

/// <summary>Validates <see cref="StartCheckoutRequest"/> — the return base URL must be absolute http(s).</summary>
public sealed class StartCheckoutRequestValidator : AbstractValidator<StartCheckoutRequest>
{
    public StartCheckoutRequestValidator()
    {
        RuleFor(x => x.ReturnBaseUrl)
            .NotEmpty()
            .Must(BeAbsoluteHttpUrl)
            .WithMessage("ReturnBaseUrl must be an absolute http(s) URL.");
    }

    // NOTE (hardening follow-up): an open-redirect here only redirects the paying user to
    // their own chosen URL after THEIR payment (low risk), but a stricter build should also
    // check this against the configured CORS allow-list rather than accepting any http(s) URL.
    private static bool BeAbsoluteHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
