namespace Retail.Api.DTOs.Requests;

/// <summary>
/// New display order for a product's gallery: the full set of the product's image ids in the
/// desired order. <c>SortOrder</c> is reassigned to each image's index in this list.
/// </summary>
public sealed record ReorderProductImagesRequest(IReadOnlyList<Guid> ImageIds);
