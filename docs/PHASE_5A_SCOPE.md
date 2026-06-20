# Phase 5A — AI: Customer Support Chatbot (Claude Tool Use): Implementation Scope

> Authoritative pre-build scope for **Phase 5A** (the chatbot slice of Epic 5,
> `PLAN.md:519` §8a; REQUIREMENTS §7). Phase 5 is **split** into **5A — chatbot**
> (this doc) and **5B — demand forecasting + order anomaly + synthetic seeder**
> (a later companion `PHASE_5B_SCOPE.md`). Where this doc disagrees with
> `PLAN.md` / `REQUIREMENTS.md` / `DATABASE_DESIGN.md`, **this doc wins for the
> phase** — deltas are listed in §3 (decisions) and §4 (drift reconciliation)
> rather than silently absorbed; a later docs pass folds them back. Companion to
> `PHASE_2_SCOPE.md` / `PHASE_3_SCOPE.md` / `PHASE_4_SCOPE.md`. Source of truth
> for the phase.

## 1. Goal & demo target

Phase 4 landed the project's AI surface and, with it, the provider-agnostic
`ILlmClient` seam. Phase 5A turns that seam into the project's marquee AI
experience: a **customer support chatbot that uses Claude Tool Use** to answer
order questions by calling real, owner-scoped backend functions — and, when the
customer asks, to **propose** a refund the customer then explicitly confirms.

**The flow (REQUIREMENTS §7.1):** a logged-in customer clicks a floating button
on any storefront page → a Tailwind/Radix **ChatDrawer** opens → they type a
question → `POST /api/v1/chat/webhook` (JWT cookie + CSRF) → the backend
loads/creates a `ChatSession`, injects a RAG-lite context of the caller's recent
orders, and calls Claude (Sonnet) with a system prompt + **tool definitions** →
Claude drives a multi-turn `tool_use` / `tool_result` loop → the backend
executes each tool **scoped to the authenticated customer**, feeds results back,
and returns the assistant's reply → the turn (including tool calls) is persisted
as `ChatMessage` rows.

**The discipline (unchanged from Phase 4, ADR-0005):** every Claude call goes
through `ILlmClient` with a **hermetic stub** behind `Ai:Mode=stub`. The whole
phase builds, tests, and demos with **zero keys and zero spend**; flipping to a
live Anthropic key is a config change with **no service-layer edits**.

**Demo (acceptance bar, `PLAN.md` §8a / REQUIREMENTS §7.1):** in stub mode — a
customer opens the drawer and asks *"where is my last order?"* → the stub
transcript drives `list_my_recent_orders` → `get_shipping_status` → a tracking
answer renders in the drawer, and the session history persists. Asking *"I want
to cancel order 10012 for a refund"* → `start_return` returns an **eligibility
proposal** → the drawer shows a **Confirm refund** card → clicking it runs the
existing audited cancel/refund flow → the order moves to Refunding/Refunded.
Flipping `Ai:Mode=live` + a real `Ai:ApiKey` yields genuine Claude tool-use over
the same backend with no code change.

## 2. Scope boundary

**In:**
- **`ChatSession` + `ChatMessage`** entities (DATABASE_DESIGN §3.20/§3.21) + `ChatMessageRole` enum; migration **`0010_chat_sessions`** (chat tables only — see §3.1 / §4).
- **Multi-turn tool loop on the existing seam:** extend `AnthropicLlmClient` to serialize/parse `tool_use` + `tool_result` content blocks (the request direction the Phase-4 file flagged as the Phase-5 TODO); add a `Chat` logical model to `AiModelMap` + a `"chat"` arm in `ResolveModel`; extend `StubLlmClient` with a deterministic chat transcript so chat tests stay hermetic.
- **`ChatService`** (depends on `ILlmClient` only): the agentic loop with a **max-turns guard**, RAG-lite context injection, prompt-injection hardening on user turns + tool results, and **200-on-failure** graceful degradation.
- **Tool dispatcher** for the four real tools — `get_order`, `list_my_recent_orders`, `get_shipping_status` (read-only, auto-execute) and `start_return` (**confirmation-gated**, see §3.3) — each **scoped to the authenticated customer** by reusing the existing owner-scoped order services. The two Phase-7 tools (`get_my_loyalty_balance`, `list_my_vouchers`) are **registered but stubbed** (return "not available yet").
- **`POST /api/v1/chat/webhook`** (`[Authorize(Roles = Customer)]` + CSRF) + chat persistence (`IChatRepository`).
- **Storefront `ChatDrawer`**: a new accessible **`sheet`** primitive (right-anchored, built on `@radix-ui/react-dialog`), a floating launcher visible only to logged-in customers, message list + input form, `useSendChatMessage` hook, mounted globally in `StorefrontShell`.
- **`start_return` confirmation flow**: the tool returns a *proposed action*; the drawer renders a **Confirm refund** card; confirming calls the **existing** customer cancel/refund endpoint (audited `OrderCancellationService`) — no new money path.
- **Admin chat-session diagnostics** page (REQUIREMENTS §7.3): read-only history viewer for any session (Administrator-only).
- **Hermetic tests** (stub provider): chat-webhook integration tests (tool loop, ownership scoping, 200-on-failure), `ChatService` unit tests (loop + max-turns), Vitest for the drawer/confirm card, a Playwright chat golden path; dev-only seeder of a couple of demo sessions.

**Out / deferred:**
- **Demand forecasting + order anomaly + 6-month synthetic seeder** → **Phase 5B** (`PHASE_5B_SCOPE.md`). 5A is chatbot-only.
- **Live key provisioning** (real Anthropic `sk-ant-…`) → deferred config-flip (§3.2); 5A ships stub-first, live-ready.
- **Token streaming** to the drawer → out. The MVP is request/response (persist turn, return text); `apiClient`/openapi-fetch can't stream and `ILlmClient.CompleteAsync` is one-shot (§18). Streaming would need a new fetch/SSE path + a streaming interface method — a later enhancement.
- **Prompt caching** (`cache_control` ephemeral) → out for 5A. The `EnableCaching` flag + `Cache*Tokens` usage fields exist but are unimplemented; the per-user RAG context makes the system prompt non-stable anyway. Revisit as a cost optimization once live (§17).
- **Post-delivery returns / RMA** → out. There is **no Return/RMA entity**; `start_return` wraps the existing **Paid-only** cancel/refund (§3.3). A true Fulfilled→return flow is net-new modeling, deferred.
- **Loyalty / voucher real data** → Phase 7 (tables don't exist); the two tools are stubs.
- **Copilot Studio (HMAC) caller** → Phase 6 stretch (PLAN §8a); the webhook contract is built so the same endpoint serves it later with a different auth arm.
- **Anonymous/guest chat** → out. Chat requires an authenticated customer (tools resolve `CustomerProfileId`); `ChatSession.CustomerProfileId` is nullable in the schema only to leave the Copilot/anonymous door open later.
- **Customer-facing tool-call bubbles** (REQUIREMENTS Task 5.2.3 lists 工具调用气泡 in the customer stream) → the customer `ChatDrawer` shows only user/assistant text + the `ConfirmReturnCard`; raw tool internals appear **only** in the Administrator diagnostics viewer (§10). Deliberate product call; revisit if a customer "show my work" UX is wanted.
- **Adding `ChatSession`/`ChatMessage` to the audit-trail allowlist** → no (high-volume conversational log; same call as `Review`). The `start_return` refund is already audited by `OrderRefundService`.

## 3. Key decisions (2026-06-20)

### 3.1 Split Phase 5: 5A chatbot now, 5B forecasting + anomaly later (user-confirmed)

**Decision:** build + review the **chatbot** as its own deliverable (this doc),
then a separate **5B** for demand forecasting + order anomaly + the synthetic
seeder. **Consequence for the migration:** the three features are independent and
DATABASE_DESIGN bundles all five tables under one design label
(`0005_chat_forecast_anomaly`). With the split, **5A's migration carries only the
two chat tables** → physical file **`0010_chat_sessions`**; 5B later adds
`0011_forecast_anomaly` for `DemandForecast`/`ReorderHint`/`OrderAnomaly`.
**Why:** the three are genuinely independent surfaces; the chatbot is the
marquee tool-use demo and benefits from landing + getting its own review pass
first; and forecasting (ML.NET SSA + `ml-train.yml` + Blob artifact) is the
riskiest/newest piece (user is newer to ML) — isolating it keeps 5A clean and
shippable.

### 3.2 Stub-first; live = Sonnet; provisioning deferred (user-confirmed)

**Decision:** build hermetic stub-first (`Ai:Mode=stub` everywhere), and when
live the chat model is **`claude-sonnet-4-6`** (`AiModelMap.Chat`). Provisioning
a real Anthropic key is a deferred config-flip. **Why:** mirrors the Phase-4
discipline and the saved **"no LLM API claim"** résumé rule — the chatbot is a
recruiter hook, not a billed dependency. Sonnet matches the existing
`AiModelMap.Copy` default, is plenty for tool routing/selection, and is the
cost/latency-sensible tier for a multi-turn loop. Going live = set `Ai:Mode=live`
+ `Ai:ApiKey`, **no code change**.

### 3.3 `start_return` = confirmation-gated, Paid-only cancel/refund (user-confirmed)

**Decision:** `start_return` does **not** move money on the model's say-so. When
Claude calls it, the dispatcher runs an **owner-scoped, Paid-only eligibility
check** and returns a *proposed action* (order number, refund amount, why
eligible/ineligible) as the `tool_result`; the assistant presents it and the
drawer renders a **Confirm refund** card. Only an explicit user click executes
the refund — **by calling the existing customer cancel/refund endpoint** backed
by `OrderCancellationService` (Paid→Refunding claim → Stripe → idempotent
`OrderRefundService` reversal + restock + audit). Read-only tools auto-execute.
**Why:** an LLM must never silently refund; routing the actual mutation through
the **existing audited path** means zero new money code and a single source of
truth for refund rules. Post-delivery (Fulfilled) orders return an ineligible
proposal with a friendly explanation (no RMA entity exists — §2 Out).

### 3.4 The agentic loop lives in `ChatService`; `ILlmClient` stays one-shot

**Decision:** keep `ILlmClient.CompleteAsync` a single stateless call; the
**loop** (call → if `StopReason == "tool_use"` execute tools → append the
assistant `tool_use` turn + a user `tool_result` turn → call again → until
`end_turn` or the max-turns cap) lives in `ChatService`. **Why:** the interface
is already shaped for this (each call takes the full `Messages` history); it
keeps the provider dumb and the orchestration testable against the stub. A
**`MaxToolTurns` cap (default 5)** prevents a runaway tool loop (no such guard
exists anywhere today). ADR-0005 + the claude-api manual-loop guidance both point
this way.

### 3.5 Chat webhook = cookie auth + CSRF, and 200-on-LLM-failure (PLAN-mandated)

**Decision:** despite the name, `/api/v1/chat/webhook` is **browser-called**, so
it is `[Authorize(Roles = Customer)]` + normal **CSRF** (the SPA `apiClient`
attaches the header automatically). It is **NOT** `[AllowAnonymous]` and **NOT**
added to `CsrfMiddleware`'s Stripe-only exemption. On an Anthropic 5xx/timeout,
`ChatService` **catches** the `ExternalServiceException` and returns
**HTTP 200** with a friendly retry message (REQUIREMENTS §7.1) — it must **not**
bubble to the 503 arm CopyGen uses, and it must catch it at **every** throw site
(Anthropic 5xx/timeout **and** the structured-output/parse path), not only the
network arm. **Why:** a chat outage should degrade
gracefully inside the conversation, not surface as a hard API error; the auth
posture matches the only legitimate CSRF exemption being Stripe's
server-to-server webhook.

### 3.6 Tools are owner-scoped by reusing the existing 3-hop pattern

**Decision:** every tool takes the **authenticated `appUserId`** (from
`ICurrentUserAccessor`, never from the model/tool input) → resolves
`CustomerProfileId` via `ICustomerProfileService.GetMyProfileAsync` → filters by
it. A tool asked about another user's order returns a **not-found** result (not a
403), matching the existing deliberate not-owned≡not-found collapse. `get_order`
/ `list_my_recent_orders` reuse `IOrderQueryService.GetMyOrderAsync` /
`GetMyOrdersAsync` verbatim; `get_shipping_status` adds an **owner-scoped**
shipment projection (the existing `GetTrackedWithShipmentAsync` is **not**
owner-scoped — must not be called directly from a customer tool). **Why:** the
tools are a new attack surface; reusing the proven owner-scoping is the safe,
DRY path. Tools key on the human **`OrderNumber`** (an `int`, what a customer
quotes) → add an owner-scoped `GetOwnedByOrderNumberAsync` that returns the
**full** `Order` (incl. `Id`, mirroring `GetOwnedByIdAsync`), so `start_return`
resolves the quoted `OrderNumber` → the order's `Id` (Guid) and puts **both** in
`ChatProposedAction`; the confirm step calls the GUID-keyed cancel route
(§3.3 / §9 / §11).

### 3.7 RAG-lite = a compact recent-orders context block; details via tools

**Decision:** prepend a small **recent-orders summary** (last 5 orders: number,
date, status, total — owner-scoped) to the system prompt for grounding;
**detailed** lookups (line items, tracking) go through the tools on demand.
**Why:** PLAN §8a says "RAG-lite (last 5 orders + 10 lines)"; injecting a tiny
summary lets Claude answer simple "what did I order" questions without a tool
round-trip while keeping the prompt small, and tools cover depth.
**Prompt-injection posture:** the *primary* defenses are **structural** — tool
arguments are JSON-schema-validated, identity is **never** taken from tool/model
input (§3.6), and `start_return` is proposal-only while the confirm endpoint
independently re-verifies Paid + ownership server-side, so injected text cannot
move money or fabricate eligibility (the refund amount + eligibility are
server-computed by the owner-scoped dispatcher). As **defense-in-depth**, the
recent-orders block + every `tool_result` are wrapped in a delimited,
data-not-instructions frame (the Phase-4 CopyGen pattern) and the system prompt
instructs Claude to treat all `tool_result` / RAG content as untrusted data,
never instructions.

### 3.8 Admin diagnostics = new `Chat.View` policy ({StoreManager, Administrator})

**Decision:** add a **new** policy `Chat.View = RequireRole(StoreManager,
Administrator)` (constant `Roles.Policies.ChatView`), mirroring the Phase-4
`Sentiment.View` precedent, and gate `GET /api/v1/chat/sessions` +
`GET /api/v1/chat/sessions/{id}` with it. **Why:** REQUIREMENTS' role matrices
(lines 36 + 377) grant chat-history viewing to **StoreManager ✅ + Administrator
✅** — Administrator-only would drop StoreManager's granted permission. No
*existing* policy fits: `UsersManageStaff` is `managerPlus` (SM+Admin) but is
semantically staff-account management, and the only Administrator-only policies
(`CatalogManage` / `UsersManageManagers`) are write-scoped — so a dedicated
`Chat.View` is the honest gate, exactly as `Sentiment.View` was added for the
sentiment dashboard. The FE mirror needs a new `ROLE_SETS.chat =
['StoreManager','Administrator']` key (do **not** reuse `catalog`). StoreManager
already sees the order workbench / refunds, so transcript access is consistent.
*(This reverses the doc's earlier Administrator-only intent — flagged by the
scope review against the REQUIREMENTS matrix + the Sentiment precedent.)*

## 4. Doc-vs-code drifts this phase fixes (recon-verified)

| # | Doc / map claims | Reality → Phase 5A action |
|---|---|---|
| 1 | DATABASE_DESIGN §5: migration is `0005_chat_forecast_anomaly` (all 5 tables) | The on-disk sequence is monotonic; last applied is `20260618030412_0009_reviews_sentiment`, and `0005` is already `0005_checkout_idempotency`. With the 5A/5B split, **5A's physical file = `<ts>_0010_chat_sessions`** (chat tables only); 5B adds `0011_forecast_anomaly`. Keep the design label only as a label. (Same handling as Phase 4's `0004`→`0009`.) |
| 2 | PLAN §8a / ADR-0005 name the live LLM as the `Anthropic.SDK` NuGet | As-built (Phase-4 §19): `AnthropicLlmClient` is a **typed `HttpClient` on the Messages REST API** (`POST /v1/messages`, `anthropic-version: 2023-06-01`). 5A follows the as-built pattern; the `using Anthropic.SDK` compile-guard is moot (no SDK namespace). |
| 3 | PLAN §8a implies `ChatService` etc. may exist | Verified: **no chat entity/service/controller exists**; only `LlmContracts.cs` comments reference "the Phase-5 ChatService". 5A builds it greenfield on the existing seam. |
| 4 | `AnthropicLlmClient` "supports tool use" | Verified **half-true**: `MapCompletion` already parses `tool_use`/`text` response blocks, but `BuildRequestBody` serializes only `m.Text` — it **drops** `LlmMessage.ToolUses`/`ToolResults`. The **request direction** is the load-bearing edit for a multi-turn loop (the file's own comment flags it). |
| 5 | DATABASE_DESIGN §3.21 `ChatMessage.Role` lists `4=Tool` | Anthropic has no "tool" role — `tool_result` rides a **User** message. Keep the enum value for the *persistence/diagnostics* label (a row that records a tool call), but the **wire** mapping never emits a "tool" role (§5.1 note). `LlmRole` (User/Assistant) is unchanged. |
| 6 | REQUIREMENTS §7.1 lists `start_return(orderNumber, lineId, reason)` (per-line) | There is no per-line return; the existing flow cancels/refunds the **whole Paid order**. 5A's `start_return(orderNumber, reason)` proposes a whole-order cancel/refund (§3.3); `lineId` is dropped. |
| 7 | DATABASE_DESIGN §3.20 `ChatSession` has no `CreatedAt`, but the index `IX_ChatMessage_ChatSessionId_CreatedAt` references `CreatedAt` | `CreatedAt` comes from `IAuditableEntity` (stamped by the interceptor), same as every entity. `ChatMessage` implements `IAuditableEntity`; the index is on `(ChatSessionId, CreatedAt)`. |
| 8 | DATABASE_DESIGN §3.20/3.21 list PK `Default = newsequentialid()` and timestamps as `datetime2(7)` | As-built convention (matches PHASE_2_SCOPE drift #7): GUID PKs are **EF/client-generated** (no DB `defaultValueSql`) and all timestamps realize as **`datetimeoffset`** (C# `DateTimeOffset`), not `datetime2`. `ChatSession`/`ChatMessage` follow the as-built convention; the `newsequentialid()` / `datetime2(7)` labels are design-table labels only. |
| 9 | PLAN §7 / REQUIREMENTS Task 5.3.1 name the diagnostics route `GET /chat/sessions/{id}/history` | 5A exposes `GET /chat/sessions/{id}` (the session resource *is* its history) **plus** a net-new `GET /chat/sessions` paged list (admin needs a list to reach a session). REST-resource shape; folded back into PLAN/REQUIREMENTS at the C4 docs pass. |

## 5. Data model — migration `0010_chat_sessions`

### 5.1 `ChatMessageRole` enum (`Common/Enums/CommerceStatuses.cs`)

`: byte` → `tinyint`, 1-based, beside `OrderStatus`/`ShipmentStatus`/`SentimentLabel`:
```
ChatMessageRole { User = 1, Assistant = 2, System = 3, Tool = 4 }
```
`Tool` labels a persisted row that records a tool call/result (for diagnostics).
It is **not** an Anthropic wire role — on the wire, `tool_result` rides a `User`
message and `tool_use` rides an `Assistant` message (§4 row 5). The
provider-facing `LlmRole` (User/Assistant only) is unchanged.

### 5.2 `ChatSession` entity (`IAuditableEntity`)

Per DATABASE_DESIGN §3.20 (with the as-built type/default conventions of §4 row 8):
- `Id` Guid PK (**client-generated**, no DB default); `CustomerProfileId` Guid **FK, NULLABLE** (`Restrict` — no cascade path; nullable only to leave the door open for Phase-6 Copilot/anonymous, but 5A always sets it); `ConversationId` `char(36)` **NOT NULL** (client-widget GUID); `StartedAt` + `LastMessageAt` C# `DateTimeOffset` → SQL **`datetimeoffset`** (the latter bumped each turn); audit columns from `IAuditableEntity`.
- **Indexes:** `UX_ChatSession_ConversationId` (**unique** — upsert key per drawer session), `IX_ChatSession_CustomerProfileId_LastMessageAt`.
- `ICollection<ChatMessage> Messages` nav.

### 5.3 `ChatMessage` entity (`IAuditableEntity`; child of `ChatSession`)

Per DATABASE_DESIGN §3.21:
- `Id` Guid PK (client-generated); `ChatSessionId` Guid **FK NOT NULL** (`Cascade` from session); `Role` `tinyint` (`ChatMessageRole`); `Content` `nvarchar(max)`; `ToolName` `nvarchar(80)?` (when `Role=Tool`); `ToolPayloadJson` `nvarchar(max)?` (tool args or result); audit columns.
- **Index:** `IX_ChatMessage_ChatSessionId_CreatedAt`.

EF configs `ChatSessionConfiguration` / `ChatMessageConfiguration`
(`IEntityTypeConfiguration<T>`, auto-discovered); `RetailDbContext` gains
`DbSet<ChatSession> ChatSessions` + `DbSet<ChatMessage> ChatMessages`. Neither is
soft-deletable (append-only log → no global query filter) and neither is on the
`AuditTrailInterceptor` allowlist.

## 6. LLM seam extension (reuse the Phase-4 `ILlmClient`)

No new interface — `ILlmClient.CompleteAsync` is reused per turn. Changes:
- **`AnthropicLlmClient.BuildRequestBody`** — emit Anthropic **content-block arrays** instead of a plain `content` string: an `Assistant` `LlmMessage` with `ToolUses` → `[{type:"text",...}?, {type:"tool_use", id, name, input}]`; a `User` `LlmMessage` with `ToolResults` → `[{type:"tool_result", tool_use_id, content}]`; a plain text message stays `[{type:"text", text}]`. Every `tool_use` id must have a matching `tool_result` in the next user turn. Do **not** emit `content: ""` on tool-bearing turns — the Messages API rejects empty-string content; emit the block array (omit the text block when there's no text).
- **`AnthropicLlmClient.MapCompletion`** — verify it tolerates an assistant turn that interleaves `text` + `tool_use` (today it overwrites `text` if multiple text blocks appear — fine for one block; concatenate to be safe).
- **`AiSettings.AiModelMap`** — add `Chat` (default `"claude-sonnet-4-6"`); **`ResolveModel`** — add a `"chat"` arm (today only `"copy"`).
- **`StubLlmClient`** — add a chat branch discriminated by **`ToolChoice?.RequiredToolName is null` (Kind `"auto"`) AND `Tools` non-empty**, evaluated **before** the existing forced-tool fallback (which always returns `tool_use`, so a chat request would otherwise mis-fire into `Tools.First()` and never reach `end_turn`). It returns a **deterministic transcript**: first call → a `tool_use` for `list_my_recent_orders` (or `get_order`), `StopReason="tool_use"`; second call (the incoming `LlmMessage` now carries `ToolResults`) → a final text turn, `StopReason="end_turn"`. Keeps chat tests hermetic and exercises the real loop. The existing forced-tool `emit_product_copy` branch (CopyGen sets `RequiredTool`) is untouched.
- **No second `ILlmClient` binding** — the same `Ai:Mode` switch serves both CopyGen and chat.

## 7. `ChatService` — the agentic loop

`IChatService` / `ChatService` (depends on `ILlmClient`, `IChatRepository`, the
owner-scoped order services, `ICurrentUserAccessor`, `ICustomerProfileService`,
`IOptions<AiSettings>`, `ILogger`):
1. Resolve `appUserId` (caller) → `CustomerProfileId`. Upsert the `ChatSession` by `ConversationId` (owner-checked — a session must belong to the caller).
2. Persist the incoming `User` `ChatMessage`; load prior turns for this session.
3. Build the `LlmRequest`: system prompt (assistant persona + guardrails + the **RAG-lite recent-orders block**, §3.7) + the message history mapped to `LlmMessage`s + the **tool definitions** (§8) + `ToolChoice.Auto` + `Model="chat"`.
4. **Loop** (≤ `MaxToolTurns`, default 5): `CompleteAsync` → if `StopReason=="tool_use"`, execute each `ToolUse` via the dispatcher (§8), append the assistant `tool_use` turn (built from `completion.ToolUses`) + a user `tool_result` turn, persist a `Tool`-role `ChatMessage` per call, and continue; else break with the final text.
5. Persist the `Assistant` `ChatMessage`, bump `LastMessageAt`, return `ChatTurnDto { reply, proposedAction? }` (`proposedAction` set when `start_return` produced an eligible proposal, §3.3).
6. **Failure:** wrap the loop; on `ExternalServiceException` from **any** throw site (Anthropic 5xx/timeout **or** structured-output/parse failure) catch and return a friendly `ChatTurnDto` with HTTP 200 (§3.5). On hitting `MaxToolTurns`, return a graceful "I couldn't complete that — please rephrase" turn.
- **Prompt-injection hardening (§3.7):** the load-bearing controls are structural (schema-validated tool args; identity never from tool input; `start_return` proposal-only + server-side Paid/ownership re-check); as defense-in-depth, user turns + tool-result content are framed as data-not-instructions and length-clamped (the CopyGen `<…>`-delimited pattern).
- **Usage logging:** log `completion.Usage` per call (token cost story); a per-request token/turn budget is enforced via `MaxToolTurns` (APIM rate-limits are Phase 6+).

## 8. Chat tools + dispatcher

A `ChatToolRegistry` defines the `LlmTool[]` (name + description + JSON-Schema
`InputSchema` built via `JsonSerializer.SerializeToElement`, the CopyGen pattern)
and an `IChatToolDispatcher` maps `ToolUse.Name` → an owner-scoped executor that
returns a string `tool_result`. **The dispatcher always passes the authenticated
`appUserId`** (§3.6); the model never supplies identity.

| Tool | Behaviour | Backs onto |
|---|---|---|
| `list_my_recent_orders()` | read-only, auto-exec; caps to 5 | `IOrderQueryService.GetMyOrdersAsync` |
| `get_order(orderNumber)` | read-only, auto-exec | new owner-scoped `GetOwnedByOrderNumberAsync` → order detail |
| `get_shipping_status(orderNumber)` | read-only, auto-exec; null shipment → "not yet shipped" | owner-scoped shipment projection (Carrier/Tracking/Status/dates) |
| `start_return(orderNumber, reason)` | **proposal only** — owner-scoped Paid-only eligibility check; returns proposed refund amount + eligibility; sets `ChatTurnDto.proposedAction`; **no mutation** | eligibility via order lookup; execution deferred to the existing cancel endpoint on confirm (§3.3) |
| `get_my_loyalty_balance()` | **stub** — "loyalty isn't available yet" | none (Phase 7) |
| `list_my_vouchers()` | **stub** — "vouchers aren't available yet" | none (Phase 7) |

A tool whose lookup misses (or isn't owned) returns a not-found `tool_result`
(§3.6), never an exception that aborts the loop; unexpected tool errors are
caught → a generic tool-failure `tool_result` so the model can recover.

## 9. Chat webhook API + persistence

- **`ChatController`** `POST /api/v1/chat/webhook` `[Authorize(Roles = Customer)]` → explicit FluentValidation (`ChatWebhookRequest { conversationId (GUID), message (1..4000) }` + validator) → `ChatService.HandleTurnAsync` → `ApiResponse<ChatTurnDto>.Ok(...)`. CSRF is automatic (no exemption). Full `ProducesResponseType` incl. 200/401/422 (no 503 — §3.5).
- **`IChatRepository` / `ChatRepository`** (scoped): `GetSessionByConversationIdAsync` (owner-scoped), `AddSessionAsync`, `AddMessageAsync`, `ListMessagesAsync`, `SaveChangesAsync` — and the admin read methods for §10 diagnostics.
- DTOs in `DTOs/Requests` + `DTOs/Responses`: `ChatWebhookRequest`, `ChatTurnDto { reply, proposedAction? }`, `ChatProposedAction { type:"confirm_return", orderId, orderNumber, refundAmountCents }`, `ChatSessionDto` / `ChatMessageDto` (diagnostics).

## 10. Frontend — `ChatDrawer` + admin diagnostics

- **New primitive `components/ui/sheet.tsx`** — copies `dialog.tsx`'s structure (Radix `Dialog.Root/Portal/Overlay/Content` + `Title` for a11y, `cn()`), but right-anchored full-height (`fixed right-0 top-0 h-full w-full max-w-md`). (No drawer/sheet primitive exists today — only the `Modal` wrapper in `components/ui/dialog.tsx`.) Adds to the "12+ accessible components" count (Job B-1).
- **`features/support/`** (mirrors the storefront reviews components — `src/features/storefront/components/ProductReviews.tsx` + `ReviewSubmitForm.tsx`, hooks under `features/storefront/hooks/`): `components/ChatDrawer.tsx` (launcher FAB + sheet + message list + input; loading/error branches like `ProductReviews`), `components/ChatMessageForm.tsx` (RHF + zod mirroring the validator + `Textarea` + `toast`), `components/ConfirmReturnCard.tsx` (renders `proposedAction`; **Confirm** calls the existing customer **cancel** mutation — `POST /orders/{orderId:guid}/cancel`, which performs the refund; **Cancel** dismisses), `hooks/useSendChatMessage.ts` (`useMutation` via `apiClient` — CSRF/JWT automatic; 503-style friendly error mapping like `useCopyGenMutation`). A `conversationId` GUID is generated per drawer open and kept in component/Zustand state.
- **Mount globally in `StorefrontShell`** (after `<main>`), gated to logged-in customers via `useAuthStore` (so it's storefront-only, excluded from `AdminShell`).
- **Admin diagnostics** `features/admin` page at `/admin/chat`: `DataTable` over `GET /chat/sessions` → row → modal/page with the full message history (`GET /chat/sessions/{id}`), read-only; gated via a **new** `ROLE_SETS.chat = ['StoreManager','Administrator']` key (don't reuse `catalog`), with a matching `RoleGuard` on the `/admin/chat` route + a `SidebarNav` item (§3.8).
- After the backend lands, `pnpm gen:api` → add DTO aliases to `lib/api/types.ts`. **Run `pnpm format` before every web push** (the separate Prettier CI gate).

## 11. API surface (new)

```
ChatController   POST /api/v1/chat/webhook            (Roles=Customer, +CSRF)   {conversationId, message} → {reply, proposedAction?}  (200 even on LLM failure)
                 GET  /api/v1/chat/sessions           (Chat.View=SM+Admin)      paged session list
                 GET  /api/v1/chat/sessions/{id}      (Chat.View=SM+Admin)      full message history
```
`start_return` confirmation reuses the **existing** customer cancel endpoint
(`POST /api/v1/orders/{orderId:guid}/cancel`, owner-scoped, backed by
`OrderCancellationService` — it performs the refund) — no new money endpoint;
`start_return` resolves the quoted `OrderNumber` → that `orderId` (§3.6). All
responses use the standard `ApiResponse<T>` envelope; lists ride `PagedResult<T>`.

## 12. Authorization design

- **Chat webhook** → `[Authorize(Roles = Customer)]` + CSRF (not anonymous, not exempt; §3.5). Back-office accounts (Staff/StoreManager/Administrator) never hold the `Customer` role, so the role attribute rejects them — that gate, **not** profile presence, is the boundary (`GetMyProfileAsync` lazily creates a profile on absence, so "no `CustomerProfile`" is **not** a security barrier and must not be relied on as one).
- **Admin diagnostics** → new `Chat.View` policy = `RequireRole(StoreManager, Administrator)` (mirrors `Sentiment.View`); no existing policy is both correctly-scoped and semantically fit (§3.8).
- **Tool authorization is domain-level, not policy-level:** every tool re-derives ownership from the authenticated principal (§3.6); `start_return` execution inherits the existing cancel endpoint's owner-scoping.
- **Not authorization:** Paid-only / eligibility for `start_return` are domain checks (friendly proposal, not 403).

## 13. Environment & secrets

- **Default everywhere** (dev / CI / tests / demo): `Ai:Mode=stub` — no keys, $0, deterministic chat transcript.
- **Live (deferred):** `Ai:Mode=live` + `Ai:ApiKey` (Anthropic `sk-ant-…`); `Ai:Models:Chat` defaults to `claude-sonnet-4-6` (overridable). `ValidateOnStart` for the key applies **outside Development only** (the Phase-4 pattern) — dev/test/CI never break on a missing key.
- `ApiFactory` already defaults `Ai:Mode=stub`; chat integration tests run against the chat-aware `StubLlmClient` with no override. The boot needs the existing `Jwt:Key` + `Csrf:Key` + `Auth:DefaultAdmin:Password` user-secrets (no new chat secret).

## 14. Testing & E2E plan

- **`ChatWebhookTests` (integration, stub):** a turn that drives the stub transcript `list_my_recent_orders → end_turn` → 200 with a reply + persisted `ChatSession`/`ChatMessage` rows (incl. a `Tool` row); anonymous → 401; bad `message` (empty / > 4000) → 422; **ownership** — a `get_order`/`start_return` for an order the caller doesn't own returns a not-found tool result (no leak, no 403); simulated `ExternalServiceException` → **200** friendly message (not 503); `MaxToolTurns` cap → graceful turn.
- **`ChatServiceTests` (unit):** the loop with a hand-rolled fake `ILlmClient` emitting `tool_use` then `end_turn`; max-turns guard; `start_return` produces a `proposedAction` and **does not** mutate; prompt-injection framing applied.
- **`AnthropicLlmClient` serialization unit test:** an `Assistant`+`ToolUses` / `User`+`ToolResults` history serializes to the correct Anthropic content-block JSON (the load-bearing §6 edit).
- **Admin diagnostics:** `Chat.View` gate (Customer/Staff → 403, **StoreManager/Administrator → 200**); `GET /chat/sessions` returns a `PagedResult` (pagination asserted); history ordering on `GET /chat/sessions/{id}`.
- **Vitest:** `ChatDrawer` (open/close, message render, role-gated launcher), `ChatMessageForm` validation, `ConfirmReturnCard` (renders proposal; Confirm fires the cancel mutation), `sheet` a11y (focus/escape).
- **Playwright (API-mocked):** open drawer → send → assistant reply renders → a `start_return` proposal → click Confirm → success toast; `@axe-core` scan of the open drawer.
- **CI:** entirely stub-mode, no new secrets; the Coverlet 85% gate continues; new tests grow the backend/Vitest/e2e counts.

## 15. Chunking (each independently buildable + verifiable)

- **C0 — Data model.** `ChatMessageRole` enum; `ChatSession`/`ChatMessage` entities + configs + `DbSet`s; migration `0010_chat_sessions`. *Verify:* build 0/0, migration applies, unique `ConversationId` index + FKs + `(ChatSessionId, CreatedAt)` index present.
- **C1 — Chatbot backend (read-only).** Extend `AnthropicLlmClient` (tool_use/tool_result content blocks) + `AiModelMap.Chat`/`ResolveModel`; chat-aware `StubLlmClient` transcript; `ChatService` loop (max-turns, 200-on-failure, RAG-lite, injection-hardening); tool registry + dispatcher for the 3 read tools + 2 stubs; `IChatRepository`; `POST /chat/webhook` (Customer + CSRF). *Verify (stub):* multi-turn round-trip with a tool call → persisted turns; ownership not-found; 200-on-failure; max-turns cap.
- **C2 — Storefront ChatDrawer.** `ui/sheet.tsx`; `features/support` drawer + launcher + `useSendChatMessage`; mount in `StorefrontShell`; `gen:api` + type aliases. *Verify:* logged-in customer opens drawer, sends, sees the stub-driven reply; launcher hidden when logged out; `pnpm format`/lint/build clean.
- **C3 — `start_return` confirmation + admin diagnostics.** `start_return` proposal path (BE eligibility + `proposedAction`); `ConfirmReturnCard` → existing cancel/refund endpoint (confirm the route); admin `/admin/chat` diagnostics (sessions list + history) + `Chat.View` policy. *Verify:* refund fires only after Confirm; Paid-only (Fulfilled → ineligible message); admin can view any session; non-admin 403.
- **C4 — Tests, seed, docs.** The full test set (§14); a Development-only seeder of ~2 demo chat sessions; reconcile DATABASE_DESIGN (fold `0010_chat_sessions` + the `4=Tool` wire note) + PLAN drifts (§4). *Verify:* all green in stub-mode CI; coverage gate holds.

## 16. Resume-bullet alignment

Per `project_resume_targets.md` + the saved **"no LLM API claim"** rule, the
chatbot is a **recruiter hook, not a headline bullet** — PLAN §7/§15 classify the
AI features as "recruiter hooks, not on either bullet," and 5A must not be
over-invested. Where 5A *does* reinforce bullets:
- **Frontend components (Job B-1):** the accessible `sheet` primitive + `ChatDrawer` + `ConfirmReturnCard` add to the "12+ reusable accessible components" count.
- **Secure backend / REST + EF (Job A-2 / B-3):** owner-scoped tool dispatch, cookie+CSRF on a state-touching endpoint, the `ChatSession`/`ChatMessage` model + repo, and reusing the audited refund path are a defensible "security-conscious API design" talking point — and survive drill-down because the *interesting* engineering (tool orchestration, ownership scoping, graceful degradation) is **not** an LLM-training claim.
- **Testing / CI (Job A-4 / B-4):** the hermetic chat-loop integration tests + serialization unit test feed the coverage / "100+ tests" / CI-on-every-PR numbers.

Net: the **async/event-driven** résumé payoff (Job A-3, "10K+ events/day")
concentrates in **5B** (the anomaly/forecast `BackgroundService`s → Phase-8
Functions) and Phase 8, not here. 5A's payoff is the **B-1 component** + the
**secure-API** story + the demo flourish.

## 17. Open items / follow-ups

- **Live-key provisioning** (real Anthropic `sk-ant-…`) — deferred config-flip (§3.2); build is live-ready behind `Ai:Mode`.
- **Prompt caching** (`cache_control` ephemeral on the system block + `Cache*Tokens` usage parsing) — deferred cost optimization once live (§2); flag + fields already exist on the contracts.
- **Token streaming** to the drawer (new fetch/SSE path + a streaming `ILlmClient` method) — deferred enhancement (§18).
- **Copilot Studio (HMAC) caller** — Phase 6 stretch; the webhook contract is built to serve it with a second auth arm.
- **Post-delivery returns / RMA entity** — out (no domain support); revisit only if a bullet needs it.
- **CSRF exemption is a path-prefix (`StartsWithSegments`) check** — when the Phase-6 Copilot/HMAC arm is added, give it its own HMAC-validated path so it does **not** inherit the Stripe exemption.
- ~~**Fold `0010_chat_sessions` + the split into DATABASE_DESIGN §5**~~ — ✅ done in C4 (DATABASE_DESIGN §5 rows 5A/5B); §8 naming + other deltas reconciled in §19. 5B will add `0011_forecast_anomaly`.

## 18. Known limitations (5A)

- **No streaming:** replies render after the full loop completes (request/response). A long multi-tool turn shows a loading state, not token-by-token output. Streaming is a deliberate later enhancement (§17).
- **In-process, single-call orchestration:** the loop runs synchronously inside the request; a 5-turn loop with a 30s-Polly-wrapped call per turn can be slow under a live provider. `MaxToolTurns` + the per-call timeout bound it; APIM rate-limits/timeouts are Phase 6+.
- **Conversation memory is per-`ConversationId`:** history is reloaded from SQL each turn; there is no summarization/truncation of very long sessions in 5A (cap message count fed to the model if needed — flagged, not built).
- **`start_return` is Paid-only:** post-delivery returns are out (no RMA entity); the tool returns an honest ineligible proposal for Fulfilled orders.
- **Transcript PII:** `ChatSession`/`ChatMessage` persist full conversations + tool payloads (order detail) with no retention limit — a long-lived PII store whose only access control is the `Chat.View` admin gate. Retention/erasure + tool-payload redaction are **not** modeled in 5A (flag for a later data-retention pass alongside Phase-9 observability/runbooks).

## 19. As-built reconciliation (C0–C4)

Phase 5A shipped C0–C4 (all on `main`, CI green: C0 `e465dc8`, C1 `2d0bdfc`, C2 `d21fa28`, C3 `ba703ce`). Deltas from this scope's pre-build plan, recorded here rather than silently absorbed:

- **Migration split.** Phase 5 was split into **5A (chatbot)** + **5B (forecasting + anomaly)**, so the chat tables shipped on their own as the physical file **`0010_chat_sessions`** (not the design label `0005_chat_forecast_anomaly`; `0005` is already `0005_checkout_idempotency`). DATABASE_DESIGN §5 now reflects this (5A `0010_chat_sessions`, 5B `0011_forecast_anomaly` planned).
- **Tool registry/dispatcher naming.** §8 named `ChatToolRegistry` / `IChatToolDispatcher`; the as-built equivalents are **`ChatTools`** (static `LlmTool[]` catalogue) + **`IChatToolExecutor`/`ChatToolExecutor`** (the owner-scoped dispatcher). Functionally identical; clearer names.
- **`ChatToolResult` seam (C3).** The executor returns **`ChatToolResult(Content, ProposedAction?)`**, not a bare string — so a confirmation-gated tool (`start_return`) can surface a `ChatProposedAction` that `ChatService` threads (reset per tool round → last-round-wins) onto `ChatTurnDto.ProposedAction`. `ChatTurnDto` gained the optional `ProposedAction` in C3 (Reply-only in C1/C2, as planned).
- **`start_return` landed in C3, not C1.** Per the chunking, C1 shipped read-only tools + the 2 Phase-7 stubs; `start_return` (proposal-only, Paid-only, owner-scoped) + its `ConfirmReturnCard` are C3. The confirm reuses the existing customer cancel endpoint `POST /api/v1/orders/{id}/cancel` — no new money path.
- **Admin RBAC = new `Chat.View` policy ({StoreManager, Administrator}).** Flipped from the doc's earlier Administrator-only intent during the scope review, to honour the REQUIREMENTS matrix + mirror `Sentiment.View` (§3.8). FE mirror: `ROLE_SETS.chat`.
- **Diagnostics routes (§4 row 9).** `GET /api/v1/chat/sessions` (paged list) + `GET /api/v1/chat/sessions/{id}` (history), not PLAN's `/sessions/{id}/history`. The DbContext-direct `ChatQueryService` mirrors `AuditQueryService`; transcript history is ordered User→Tool→Assistant in memory (a single SaveChanges stamps one `CreatedAt` across the turn).
- **FE confirm flow.** `ConfirmReturnCard` is presentational; the `useCancelOrder` mutation is owned by `ChatDrawer` (so a refund can't silently complete if the customer sends a new turn mid-cancel, and the composer locks while confirming) — a C3-review hardening.
- **Dev demo seed.** `ChatDemoSeeder` (Development-only, idempotent) seeds 2 demo chat sessions so the admin diagnostics page shows data on first run.
- **Tests as-built.** ~28 chat-focused tests across the chunks (chat-webhook + tool-executor + diagnostics integration; `AnthropicLlmClient` wire serialization + `ChatService` loop/failure/proposal unit; storefront drawer + confirm-card + admin-page + role-set Vitest) — backend 250 + web 48 green, stub-mode CI, 85% coverage gate held.
