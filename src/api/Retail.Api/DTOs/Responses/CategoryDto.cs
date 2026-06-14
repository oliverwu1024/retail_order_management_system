namespace Retail.Api.DTOs.Responses;

/// <summary>A category as returned to clients.</summary>
public sealed record CategoryDto(Guid Id, string Slug, string Name, Guid? ParentId);
