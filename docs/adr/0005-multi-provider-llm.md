# ADR-0005: Multi-Provider LLM Abstraction (`ILlmClient`)

**Status**: Accepted (2026-06-06)

**Deciders**: project owner

**Related**: PLAN.md §5 (Tech Stack), §8a (Chatbot), §8b (Copy gen), §15 risk #4 & #6 · REQUIREMENTS.md §6.2, §6.3 · CODING_STANDARDS § AI Client 抽象

---

## Context

Two AI features in this project call a chat-completion LLM:

1. **Customer support chatbot** (PLAN §8a) — multi-turn, tool use (`get_order`, `list_my_recent_orders`, `get_shipping_status`, `start_return`), system prompt caching.
2. **Product copy / SEO generation** (PLAN §8b) — single-turn, single tool (`emit_product_copy`) forced via `tool_choice` to guarantee structured JSON output.

The primary provider locked in `tech_decisions` is **Anthropic Claude (Sonnet)** via the community `Anthropic.SDK` NuGet. We considered also implementing **OpenAI** as a second provider for breadth. Building both during the 8-week window (Phases 4 and 5) would double prompt-tuning, key management, and billing complexity for **no user-visible benefit** — see PLAN.md §15 risk #6 (scope creep across the four AI features is the dominant risk).

At the same time:

- The two features should not directly couple to Anthropic SDK types (`Anthropic.SDK.Messaging.MessageRequest`, etc.). If we ever swap or add a provider, we don't want to rewrite both `CopyGenService` and `ChatService`.
- For the resume / interview story, demonstrating a clean provider abstraction with one shipping implementation is more valuable than a half-finished second provider.
- Risk #4 (Anthropic outage during demo) is currently mitigated by `Ai:Mode=live|stub`; a clean interface adds `Ai:Provider=anthropic|openai` as a real second axis if/when OpenAI lands.

## Decision

Both AI features call the LLM through a small, provider-agnostic interface owned by us:

```csharp
public interface ILlmClient
{
    Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken ct);
}
```

`LlmRequest`, `LlmCompletion`, `LlmTool`, `LlmToolUse`, `LlmToolResult`, `LlmToolChoice`, and `LlmUsage` are **our own records** in `Retail.Api/Ai/Contracts/` — not SDK types. Field shapes follow the cross-provider lowest-common-denominator (messages + tools + tool_choice + usage tokens). See CODING_STANDARDS § AI Client 抽象 for full shape and a `CopyGenService` example.

**For Phases 4–5 (Anthropic primary, ships):**

- One concrete implementation: `AnthropicLlmClient` in `Retail.Api/Ai/Providers/`.
- DI registered as the only `ILlmClient` binding.
- `CopyGenService` and `ChatService` depend on `ILlmClient` only, never `using Anthropic.SDK`.

**For Phase 6/7 stretch (OpenAI, optional):**

- Add `OpenAiLlmClient` alongside `AnthropicLlmClient` (~1 day of work given the interface is fixed).
- Switch via `Ai:Provider=anthropic|openai` config, resolved at DI registration.
- Zero service-layer changes required.

## Consequences

**Positive**

- `CopyGenService` and `ChatService` insulated from provider churn (NuGet bumps, model renames, API schema changes).
- Resume / interview narrative: *designed for multi-provider LLM swap, shipped one provider in scope, second as a stretch goal.*
- Risk #4 (Anthropic outage) gets a real fallback path beyond canned stubs — flip `Ai:Provider`, not just `Ai:Mode=stub`.
- Trivial to unit-test (`Mock<ILlmClient>` instead of mocking the Anthropic SDK's request/response types).
- Cost telemetry is uniform: `llm.provider`, `llm.model`, `llm.input_tokens`, `llm.output_tokens`, `llm.cache_read_tokens` as Serilog properties regardless of provider.

**Negative / trade-offs**

- One layer of DTO translation between our types and the Anthropic SDK types. Mitigation: keep `AnthropicLlmClient` to ~100 lines of pure mapping; no business logic.
- Lowest-common-denominator interface cannot directly expose provider-specific features (Claude's `cache_control` breakpoints, OpenAI's `logprobs`, Anthropic's extended thinking). Mitigation: provider-specific optimizations live **inside** `AnthropicLlmClient.CompleteAsync`. The caller asks for `EnableCaching = true`; the provider decides how to mark the prompt.
- Slight up-front boilerplate (the DTOs in `Ai/Contracts/`). Acceptable given the abstraction is small and well-bounded (one method, eight records).

## Alternatives considered

1. **Direct dependency on `Anthropic.SDK` in `CopyGenService` and `ChatService`.**
   - Rejected: couples business logic to vendor SDK; harder to test; weaker portfolio story; provider swap becomes a multi-day rewrite.

2. **Build both Anthropic and OpenAI providers now (Phases 4 + 5).**
   - Rejected: doubles prompt-tuning, key management, and billing surface across two features that already span two phases. PLAN.md §15 risk #6 explicitly calls out scope creep across AI features as the top risk. Second provider can ship in Phase 6/7 with no architectural rework if the interface lands now.

3. **Use `Microsoft.Extensions.AI` (`IChatClient`) as the abstraction.**
   - Rejected for the 2026 build; revisit at .NET 10 LTS bump (Nov 2026). As of June 2026, `Microsoft.Extensions.AI` is GA but the ecosystem of community provider implementations is uneven, and there is no first-party `IChatClient` adapter for `Anthropic.SDK`. Our `ILlmClient` and `LlmRequest`/`LlmCompletion` shapes deliberately mirror `IChatClient`'s `ChatMessage`/`ChatCompletion` so the migration is mechanical when we do it.

## Implementation notes

- `AnthropicLlmClient` registered via `IHttpClientFactory` with `AddStandardResilienceHandler()` (Polly 8). See CODING_STANDARDS § Resilience.
- **Model alias mapping**: `LlmRequest.Model` is a logical name (`"chat"`, `"copy"`) resolved by the provider via `IOptions<AiSettings>` to a real model ID (e.g. `claude-sonnet-4-6-20251101`). This insulates services from model renames and lets per-feature model selection happen in config.
- **Prompt caching** (chatbot only): when `LlmRequest.EnableCaching = true`, the Anthropic provider appends `cache_control = { type = "ephemeral" }` to the last system content block. Copy gen is single-turn and does not enable caching.
- **Tool-choice mapping**:
  - `LlmToolChoice.Auto` → Anthropic `{"type": "auto"}` (chatbot)
  - `LlmToolChoice.RequiredTool("emit_product_copy")` → Anthropic `{"type": "tool", "name": "emit_product_copy"}` (copy gen — guarantees structured output)
- **Failure mapping**: provider implementations translate transport errors to `ExternalServiceException` with error code `EXTERNAL_SERVICE_UNAVAILABLE` (per CODING_STANDARDS § error codes). The middleware returns `503` to clients.
- **`Ai:Mode=stub`**: there is a `StubLlmClient` (returns canned fixtures from `tests/fixtures/ai/`) registered when `Ai:Mode = stub`. Same interface — `ILlmClient` — so services are unaware.
- **Compile-time guard**: a `Directory.Build.targets` rule prevents `using Anthropic.SDK` outside `Retail.Api/Ai/Providers/`. Enforces the abstraction at build time, not just review time.

## Revisit triggers

- We need a Claude-specific feature (extended thinking, batch API, files, citations) that does not fit the LCD interface. → Extend `LlmRequest`/`LlmCompletion` rather than break the abstraction.
- We bump to .NET 10 LTS and `Microsoft.Extensions.AI` has a mature `Anthropic.SDK` adapter. → Migrate `ILlmClient` → `IChatClient`.
- OpenAI ships in Phase 6/7. → Promote `Ai:Provider` from "future flag" to documented operational setting; add an integration test for both providers running the copy-gen happy path.
