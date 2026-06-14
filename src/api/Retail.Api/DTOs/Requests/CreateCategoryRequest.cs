namespace Retail.Api.DTOs.Requests;

/// <summary>Admin payload to create a category. <paramref name="Slug"/> is auto-generated from the name if omitted.</summary>
public sealed record CreateCategoryRequest(string Name, string? Slug, Guid? ParentId);
