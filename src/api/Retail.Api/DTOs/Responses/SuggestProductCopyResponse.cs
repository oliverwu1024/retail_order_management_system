namespace Retail.Api.DTOs.Responses;

/// <summary>
/// LLM-generated product copy returned by <c>generate-copy</c> (Phase 4, Story 4.2). The admin
/// reviews it and chooses what to accept into the product — the API never persists it.
/// </summary>
public sealed record SuggestProductCopyResponse(
    string Description,
    string SeoTitle,
    string SeoMetaDescription,
    IReadOnlyList<string> BulletPoints);
