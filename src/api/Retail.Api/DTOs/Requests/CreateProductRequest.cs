namespace Retail.Api.DTOs.Requests;

/// <summary>
/// Admin payload to create a product (REQUIREMENTS §2.2). <paramref name="Slug"/> is
/// auto-generated from the name if omitted. Variants are added separately via
/// <c>POST /products/{id}/variants</c>.
/// </summary>
public sealed record CreateProductRequest(
    string Sku,
    string Name,
    string? Slug,
    string? Description,
    string? SeoTitle,
    string? SeoDescription,
    string? BrandName,
    Guid CategoryId,
    bool IsPublished);
