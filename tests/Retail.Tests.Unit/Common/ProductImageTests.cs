using System.Text;
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

    [Fact]
    public void TryDetectContentType_PngBytes_DetectsPng()
    {
        byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };

        Assert.True(ProductImage.TryDetectContentType(png, out string contentType));
        Assert.Equal("image/png", contentType);
    }

    [Fact]
    public void TryDetectContentType_JpegBytes_DetectsJpeg()
    {
        byte[] jpeg = { 0xFF, 0xD8, 0xFF, 0xE0 };

        Assert.True(ProductImage.TryDetectContentType(jpeg, out string contentType));
        Assert.Equal("image/jpeg", contentType);
    }

    [Fact]
    public void TryDetectContentType_WebpBytes_DetectsWebp()
    {
        byte[] webp = { (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0, 0, 0, 0, (byte)'W', (byte)'E', (byte)'B', (byte)'P' };

        Assert.True(ProductImage.TryDetectContentType(webp, out string contentType));
        Assert.Equal("image/webp", contentType);
    }

    [Fact]
    public void TryDetectContentType_HtmlSpoofingAnImage_ReturnsFalse()
    {
        // A payload that lies about being an image (declared image/png, real bytes are HTML)
        // must fail detection so it is never stored under an image content type.
        byte[] html = Encoding.ASCII.GetBytes("<!DOCTYPE html><script>x</script>");

        Assert.False(ProductImage.TryDetectContentType(html, out string contentType));
        Assert.Equal(string.Empty, contentType);
    }

    [Fact]
    public void TryDetectContentType_TooShortToIdentify_ReturnsFalse()
    {
        Assert.False(ProductImage.TryDetectContentType(new byte[] { 0xFF }, out _));
    }
}
