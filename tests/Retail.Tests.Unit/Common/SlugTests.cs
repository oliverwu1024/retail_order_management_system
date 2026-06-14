using Retail.Api.Common.Helpers;

namespace Retail.Tests.Unit.Common;

/// <summary>Unit tests for the URL-slug generator.</summary>
public class SlugTests
{
    [Theory]
    [InlineData("Acme Running Shoe", "acme-running-shoe")]
    [InlineData("  Trimmed  ", "trimmed")]
    [InlineData("Multiple   spaces", "multiple-spaces")]
    [InlineData("Special!@#Chars", "special-chars")]
    [InlineData("UPPER case", "upper-case")]
    [InlineData("already-a-slug", "already-a-slug")]
    [InlineData("Trailing---", "trailing")]
    public void From_NormalizesToUrlSafeSlug(string input, string expected)
    {
        Assert.Equal(expected, Slug.From(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!@#$%")]
    public void From_WithNoAlphanumerics_ReturnsEmpty(string input)
    {
        Assert.Equal(string.Empty, Slug.From(input));
    }
}
