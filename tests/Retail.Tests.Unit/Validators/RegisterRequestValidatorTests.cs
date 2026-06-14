using Retail.Api.DTOs.Requests;
using Retail.Api.Validators;

namespace Retail.Tests.Unit.Validators;

/// <summary>
/// Pins the registration rules from REQUIREMENTS §1.1: valid email, non-empty
/// display name, and a password of ≥12 chars containing at least one letter and
/// one digit.
/// </summary>
public class RegisterRequestValidatorTests
{
    private readonly RegisterRequestValidator _validator = new();

    [Theory]
    [InlineData("", "Sup3rSecret!pw", "Tester")]              // missing email
    [InlineData("not-an-email", "Sup3rSecret!pw", "Tester")]  // malformed email
    [InlineData("u@test.local", "short1", "Tester")]          // password < 12 chars
    [InlineData("u@test.local", "alllettersnodigits", "Tester")] // no digit
    [InlineData("u@test.local", "123456789012", "Tester")]    // no letter
    [InlineData("u@test.local", "Sup3rSecret!pw", "")]        // missing display name
    public void Invalid_Inputs_FailValidation(string email, string password, string displayName)
    {
        var result = _validator.Validate(new RegisterRequest(email, password, displayName));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void WellFormed_Input_Passes()
    {
        var result = _validator.Validate(new RegisterRequest("u@test.local", "Sup3rSecret!pw", "Tester"));

        Assert.True(result.IsValid);
    }
}
