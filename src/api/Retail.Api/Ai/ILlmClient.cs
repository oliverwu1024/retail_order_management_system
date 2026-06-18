using Retail.Api.Ai.Contracts;

namespace Retail.Api.Ai;

/// <summary>
/// The single seam every LLM-backed feature calls through (ADR-0005). One method; the concrete
/// provider (Anthropic live, or the hermetic stub) is chosen by <c>Ai:Mode</c> at DI time, so
/// services never reference a provider type.
/// </summary>
public interface ILlmClient
{
    /// <summary>Runs a completion. Throws <c>ExternalServiceException</c> (→ 503) if the live provider is unavailable.</summary>
    Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken ct);
}
