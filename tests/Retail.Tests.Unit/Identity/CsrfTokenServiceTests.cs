using Microsoft.Extensions.Options;
using Retail.Api.Identity;

namespace Retail.Tests.Unit.Identity;

/// <summary>
/// Unit tests for <see cref="CsrfTokenService"/> — the signed double-submit CSRF
/// token. The signature is what makes the token unforgeable, so the tests pin
/// round-trip, tamper-rejection, and cross-key rejection.
/// </summary>
public class CsrfTokenServiceTests
{
    private static CsrfTokenService NewService(string key = "csrf-test-key-0123456789-abcdefghijkl") =>
        new(Options.Create(new CsrfOptions { Key = key }));

    [Fact]
    public void IssueThenValidate_RoundTrips()
    {
        CsrfTokenService sut = NewService();

        string token = sut.Issue();

        Assert.True(sut.Validate(token));
    }

    [Fact]
    public void Validate_TamperedRandomPart_ReturnsFalse()
    {
        CsrfTokenService sut = NewService();
        string token = sut.Issue();
        string[] parts = token.Split('.');

        // Mutate the random half; the signature no longer matches.
        string tampered = parts[0] + "x." + parts[1];

        Assert.False(sut.Validate(tampered));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-dot-here")]
    [InlineData("too.many.dots")]
    public void Validate_MalformedToken_ReturnsFalse(string? token)
    {
        Assert.False(NewService().Validate(token));
    }

    [Fact]
    public void Validate_TokenSignedWithDifferentKey_ReturnsFalse()
    {
        CsrfTokenService issuer = NewService("key-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        CsrfTokenService validator = NewService("key-bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

        string token = issuer.Issue();

        Assert.False(validator.Validate(token));
    }
}
