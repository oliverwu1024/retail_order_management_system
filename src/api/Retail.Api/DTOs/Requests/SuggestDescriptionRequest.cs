namespace Retail.Api.DTOs.Requests;

/// <summary>
/// Body for <c>POST /api/v1/catalog/products/{id}/generate-copy</c> (Phase 4, Story 4.2). Steers the
/// LLM; the product is taken from the route. The generated copy is returned, never auto-saved.
/// </summary>
public sealed record SuggestDescriptionRequest
{
    /// <summary>Voice of the copy: <c>playful</c> | <c>professional</c> | <c>luxury</c>.</summary>
    public string Tone { get; init; } = "professional";

    /// <summary>Target length: <c>short</c> | <c>medium</c> | <c>long</c>.</summary>
    public string Length { get; init; } = "medium";
}
