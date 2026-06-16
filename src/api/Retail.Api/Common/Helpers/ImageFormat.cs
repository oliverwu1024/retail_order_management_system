namespace Retail.Api.Common.Helpers;

/// <summary>
/// Rules for product-image uploads (REQUIREMENTS §2.2): the accepted content types
/// (jpg/png/webp) and the size cap, plus the content-type → file-extension mapping
/// used when naming the blob. (Renamed from <c>ProductImage</c> so that name is free for the
/// <see cref="Domain.Entities.ProductImage"/> gallery entity.)
/// </summary>
public static class ImageFormat
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

    /// <summary>
    /// Detects the real image type from the leading magic bytes — the AUTHORITATIVE check,
    /// since the client-supplied Content-Type is spoofable. Returns the canonical content
    /// type so callers store the blob with a type that matches its actual bytes.
    /// </summary>
    public static bool TryDetectContentType(ReadOnlySpan<byte> header, out string contentType)
    {
        // JPEG: FF D8 FF
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            contentType = "image/jpeg";
            return true;
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (header.Length >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            contentType = "image/png";
            return true;
        }

        // WebP: bytes 0-3 "RIFF" and bytes 8-11 "WEBP"
        if (header.Length >= 12
            && header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
            && header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P')
        {
            contentType = "image/webp";
            return true;
        }

        contentType = string.Empty;
        return false;
    }
}
