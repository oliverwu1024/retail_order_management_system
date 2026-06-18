namespace Retail.Api.Ai;

/// <summary>
/// Azure AI Language credentials, bound from the <c>Ai:AzureLanguage</c> section. Only used when
/// <c>Ai:Mode=live</c>. Empty in stub mode; supplied via user-secrets / Key Vault for live.
/// </summary>
/// <remarks>
/// Deliberately NOT validated at boot: sentiment is a background feature, not a request-path boot
/// requirement, so a missing key must not crash startup. The hosted service's per-review try/catch
/// leaves the review unscored (<c>ProcessedAt = null</c>) and the slow re-scan retries — far more
/// robust for a background path than failing the whole process.
/// </remarks>
public sealed class AzureAiLanguageOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Ai:AzureLanguage";

    /// <summary>Resource endpoint, e.g. <c>https://&lt;name&gt;.cognitiveservices.azure.com</c>.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Resource key (<c>Ocp-Apim-Subscription-Key</c>).</summary>
    public string ApiKey { get; set; } = string.Empty;
}
