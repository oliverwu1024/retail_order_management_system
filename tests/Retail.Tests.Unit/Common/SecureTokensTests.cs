using Retail.Api.Common.Helpers;

namespace Retail.Tests.Unit.Common;

/// <summary>
/// Unit tests for <see cref="SecureTokens"/> — the crypto primitives behind both
/// refresh tokens and CSRF tokens. Getting these wrong (a weak RNG, a leaky
/// compare, a non-deterministic hash) would silently undermine the whole auth
/// design, so they are pinned here.
/// </summary>
public class SecureTokensTests
{
    [Fact]
    public void NewToken_IsUrlSafe_AndDistinctPerCall()
    {
        string a = SecureTokens.NewToken();
        string b = SecureTokens.NewToken();

        Assert.NotEqual(a, b);
        Assert.DoesNotContain("+", a);
        Assert.DoesNotContain("/", a);
        Assert.DoesNotContain("=", a);
    }

    [Fact]
    public void Sha256_IsDeterministic_AndUppercaseHex()
    {
        string hash1 = SecureTokens.Sha256("hello");
        string hash2 = SecureTokens.Sha256("hello");

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length); // 32 bytes → 64 hex chars
        Assert.Matches("^[0-9A-F]+$", hash1);
    }

    [Fact]
    public void Sha256_DifferentInputs_ProduceDifferentHashes()
    {
        Assert.NotEqual(SecureTokens.Sha256("a"), SecureTokens.Sha256("b"));
    }

    [Fact]
    public void FixedTimeEquals_TrueOnlyForIdenticalStrings()
    {
        Assert.True(SecureTokens.FixedTimeEquals("same-value", "same-value"));
        Assert.False(SecureTokens.FixedTimeEquals("same-value", "diff-value"));
        Assert.False(SecureTokens.FixedTimeEquals("short", "much-longer-string"));
    }
}
