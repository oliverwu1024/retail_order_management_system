namespace Retail.Api.Common.Helpers;

/// <summary>
/// Rules for product-image uploads (REQUIREMENTS §2.2): the accepted content types
/// (jpg/png/webp) and the size cap, plus the content-type → file-extension mapping
/// used when naming the blob.
/// </summary>
public static class ProductImage
{
    /// <summary>Maximum accepted upload size (5 MB).</summary>
    public const long MaxBytes = 5 * 1024 * 1024;

    private static readonly Dictionary<string, string> ExtensionByContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
        ["image/webp"] = "webp",
    };

    /// <summary>Whether the content type is an accepted product-image format.</summary>
    public static bool IsAllowedContentType(string? contentType) =>
        contentType is not null && ExtensionByContentType.ContainsKey(contentType);

    /// <summary>File extension for an accepted content type (e.g. <c>image/jpeg</c> → <c>jpg</c>).</summary>
    public static string ExtensionFor(string contentType) => ExtensionByContentType[contentType];
}
