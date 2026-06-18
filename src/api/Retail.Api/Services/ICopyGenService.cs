using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>AI product copy generation (Phase 4, Story 4.2). Returns suggested copy; never persists it.</summary>
public interface ICopyGenService
{
    /// <summary>Generates copy for a product (404 if it doesn't exist; 503 if the AI provider fails).</summary>
    Task<SuggestProductCopyResponse> GenerateAsync(Guid productId, SuggestDescriptionRequest request, CancellationToken ct);
}
