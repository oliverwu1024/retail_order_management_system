using Microsoft.AspNetCore.Identity;
using Retail.Api.Domain.Entities;

namespace Retail.Tests.Unit.Identity;

/// <summary>
/// Pins the behaviour of the password hasher Identity uses under the hood for our
/// <see cref="ApplicationUser"/>: passwords are salted (never stored in plaintext,
/// two hashes of the same password differ) and verification succeeds only for the
/// correct password. This is the "密码 hash" coverage called for in Task 1.1.6.
/// </summary>
public class PasswordHasherTests
{
    private static readonly PasswordHasher<ApplicationUser> Hasher = new();
    private static readonly ApplicationUser User = new() { Id = "u1" };
    private const string Password = "Sup3rSecret!pw";

    [Fact]
    public void HashPassword_IsNotPlaintext_AndSaltedPerCall()
    {
        string hash1 = Hasher.HashPassword(User, Password);
        string hash2 = Hasher.HashPassword(User, Password);

        Assert.NotEqual(Password, hash1);
        Assert.NotEqual(hash1, hash2); // a fresh random salt per hash
    }

    [Fact]
    public void VerifyHashedPassword_SucceedsForCorrect_FailsForWrong()
    {
        string hash = Hasher.HashPassword(User, Password);

        Assert.Equal(PasswordVerificationResult.Success, Hasher.VerifyHashedPassword(User, hash, Password));
        Assert.Equal(PasswordVerificationResult.Failed, Hasher.VerifyHashedPassword(User, hash, "wrong-password-1"));
    }
}
