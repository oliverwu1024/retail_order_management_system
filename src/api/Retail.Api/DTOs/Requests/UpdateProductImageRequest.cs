namespace Retail.Api.DTOs.Requests;

/// <summary>
/// Edits a gallery image. The PUT carries the desired state for the editable fields:
/// <see cref="AltText"/> and <see cref="ProductVariantId"/> are set as given (null clears /
/// makes the image general). Set <see cref="IsPrimary"/> to <c>true</c> to promote this image to
/// the product's hero (the previous primary is demoted); <c>null</c>/<c>false</c> leaves the
/// primary unchanged — you promote a different image rather than "unset" the primary.
/// </summary>
public sealed record UpdateProductImageRequest(
    string? AltText,
    Guid? ProductVariantId,
    bool? IsPrimary);
