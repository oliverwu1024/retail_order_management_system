using Retail.Api.Common.Helpers;

namespace Retail.Tests.Unit.Common;

/// <summary>
/// Unit tests for <see cref="LogPathSanitizer"/> (review item P2-S2): the Stripe guest-order session
/// id must be masked out of any path before it reaches the logs.
/// </summary>
public class LogPathSanitizerTests
{
    [Theory]
    [InlineData("/api/v1/orders/by-session/cs_test_abc123", "/api/v1/orders/by-session/***")]
    [InlineData("/API/V1/ORDERS/BY-SESSION/cs_test_abc123", "/API/V1/ORDERS/BY-SESSION/***")] // case-insensitive marker
    public void Sanitize_MasksSessionId(string input, string expected) =>
        Assert.Equal(expected, LogPathSanitizer.Sanitize(input));

    [Fact]
    public void Sanitize_DoesNotLeakTheSessionId() =>
        Assert.DoesNotContain("cs_test_abc123", LogPathSanitizer.Sanitize("/api/v1/orders/by-session/cs_test_abc123"));

    [Theory]
    [InlineData("/api/v1/orders/123e4567-e89b-12d3-a456-426614174000")]
    [InlineData("/api/v1/catalog/products")]
    public void Sanitize_LeavesUnrelatedPathsUnchanged(string path) =>
        Assert.Equal(path, LogPathSanitizer.Sanitize(path));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Sanitize_NullOrEmpty_ReturnsEmpty(string? path) =>
        Assert.Equal(string.Empty, LogPathSanitizer.Sanitize(path));
}
