using Retail.Api.DTOs.Requests;
using Retail.Api.Validators;

namespace Retail.Tests.Unit.Validators;

/// <summary>
/// Login validation is shape-only (a well-formed email and a non-empty password);
/// whether the credentials are correct is the service's job.
/// </summary>
public class LoginRequestValidatorTests
{
    private readonly LoginRequestValidator _validator = new();

    [Theory]
    [InlineData("", "anything")]
    [InlineData("not-an-email", "anything")]
    [InlineData("u@test.local", "")]
    public void Invalid_Inputs_FailValidation(string email, string password)
    {
        var result = _validator.Validate(new LoginRequest(email, password));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void WellFormed_Input_Passes()
    {
        var result = _validator.Validate(new LoginRequest("u@test.local", "any-password"));

        Assert.True(result.IsValid);
    }
}
