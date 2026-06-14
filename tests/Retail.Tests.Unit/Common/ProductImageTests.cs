using Retail.Api.Common.Helpers;

namespace Retail.Tests.Unit.Common;

/// <summary>Unit tests for the product-image content-type rules.</summary>
public class ProductImageTests
{
    [Theory]
    [InlineData("image/jpeg", true)]
    [InlineData("image/png", true)]
    [InlineData("image/webp", true)]
    [InlineData("IMAGE/PNG", true)] // case-insensitive
    [InlineData("image/gif", false)]
    [InlineData("text/plain", false)]
    [InlineData("application/pdf", false)]
    [InlineData(null, false)]
    public void IsAllowedContentType_AcceptsOnlyJpegPngWebp(string? contentType, bool expected)
    {
        Assert.Equal(expected, ProductImage.IsAllowedContentType(contentType));
    }

    [Theory]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/png", "png")]
    [InlineData("image/webp", "webp")]
    public void ExtensionFor_MapsContentTypeToExtension(string contentType, string expected)
    {
        Assert.Equal(expected, ProductImage.ExtensionFor(contentType));
    }
}
