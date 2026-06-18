namespace Retail.Api.Ai;

/// <summary>
/// AI provider settings, bound from the <c>Ai</c> configuration section (ADR-0005).
/// </summary>
/// <remarks>
/// <para>
/// <b>Stub-first.</b> <see cref="Mode"/> defaults to <c>"stub"</c>, so on a fresh clone — and in
/// every test / CI run — the AI features resolve <c>StubLlmClient</c> and run with no API key and
/// no network. Flipping to live is a config change (<c>Ai:Mode=live</c> + <c>Ai:ApiKey</c>), with
/// no service-layer edit: <c>CopyGenService</c> only ever sees <c>ILlmClient</c>.
/// </para>
/// <para>
/// In Development the key is NOT validated at boot (stub is the default; the feature isn't a boot
/// requirement). Outside Development, if <c>Mode=live</c> the key IS required at boot (see
/// <c>Program.cs</c>), mirroring Stripe/Jwt — a blank key would fail every AI call silently.
/// </para>
/// </remarks>
public sealed class AiSettings
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Ai";

    /// <summary><c>"live"</c> (call the provider) or <c>"stub"</c> (canned fixtures). Defaults to stub.</summary>
    public string Mode { get; set; } = "stub";

    /// <summary>Provider id; only <c>"anthropic"</c> ships in Phase 4–5 (ADR-0005). OpenAI is a later stretch.</summary>
    public string Provider { get; set; } = "anthropic";

    /// <summary>Provider API key (<c>sk-ant-…</c>). Empty in stub mode; supplied via user-secrets / Key Vault for live.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Logical-name → real-model-id map, so services name <c>"copy"</c> and config picks the model.</summary>
    public AiModelMap Models { get; set; } = new();

    /// <summary><c>true</c> when <see cref="Mode"/> is <c>"live"</c> (case-insensitive).</summary>
    public bool IsLive => string.Equals(Mode, "live", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Maps logical model names used by services to concrete provider model ids.</summary>
public sealed class AiModelMap
{
    /// <summary>Model backing product copy generation. Anthropic Claude Sonnet (ADR-0005).</summary>
    public string Copy { get; set; } = "claude-sonnet-4-6";
}
