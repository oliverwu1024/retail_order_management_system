# Phase 4 — AI: Product Copy Generation + Reviews + Sentiment: Implementation Scope

> Authoritative pre-build scope for Phase 4 (Epic 4, `PLAN.md:511`; REQUIREMENTS
> §6 + §8). Where this doc disagrees with `PLAN.md` / `REQUIREMENTS.md` /
> `DATABASE_DESIGN.md`, **this doc wins for the phase** — deltas are listed in §3
> (decisions) and §4 (drift reconciliation) rather than silently absorbed; a
> later docs pass folds them back. Companion to `PHASE_2_SCOPE.md` /
> `PHASE_3_SCOPE.md`. Source of truth for the phase.

## 1. Goal & demo target

Phases 1–3 built the store, the checkout, and the back-office. Phase 4 adds the
project's **AI surface** — three independent features bound by a shared
provider-abstraction discipline:

1. **Customer reviews** — a verified buyer rates (1–5) and writes a review; the product page shows reviews + average.
2. **AI copy generation** — an admin clicks *Suggest Description* on the product editor; an LLM drafts `description` / SEO / bullet points behind a tool-forced JSON contract; the admin accepts or rejects (never auto-saved).
3. **Review sentiment** — each new review is scored asynchronously by Azure AI Language; an admin sentiment tile + a *Products Needing Attention* panel surface the aggregate.

**The discipline:** every external AI call goes through an abstraction
(`ILlmClient` for the LLM, `ITextAnalyticsAdapter` for sentiment) with a
**hermetic stub/fake** behind `Ai:Mode=stub`. The whole phase builds, tests,
and demos with **zero keys and zero spend**; flipping to live providers is a
config change with **no service-layer edits** (ADR-0005).

**Demo (acceptance bar, `PLAN.md` §8d / REQUIREMENTS §6):** in stub mode — an
admin generates copy for **3 products** and accepts one into the description
field; a logged-in customer who bought a product submits a review and sees it
listed with the running average; the background service scores the seeded
reviews so the admin **sentiment tile** renders a label distribution and the
**Products Needing Attention** panel lists the products whose average
`SentimentScore < −0.2`. Flipping `Ai:Mode=live` + real keys yields real Claude
copy and real Azure sentiment with no code change.

## 2. Scope boundary

**In:**
- **`Review`** entity (member-only, purchase-verified) + migration `0009_reviews_sentiment`; soft-delete + one-review-per-customer-per-product.
- **Customer review API**: `POST /products/{id}/reviews` (verified purchase) + `GET /products/{id}/reviews` (paged, public); storefront review list + submit form (rating stars + textarea).
- **`ILlmClient` abstraction** (ADR-0005): provider-agnostic `Ai/Contracts/` records + `AnthropicLlmClient` (live, behind `Ai:Mode`) + `StubLlmClient` (canned fixtures). Compile-time guard keeps `using Anthropic.SDK` inside `Ai/Providers/`.
- **`CopyGenService`** + `POST /catalog/products/{id}/generate-copy` (Administrator-only), tool-forced `emit_product_copy` JSON; admin *Suggest Description* button + tone/length modal + diff / Accept-Reject view, writing back via the existing PUT (never auto-saves).
- **`ITextAnalyticsAdapter` abstraction** + `AzureTextAnalyticsAdapter` (live, Azure AI Language F0) + `StubTextAnalyticsAdapter`.
- **Sentiment pipeline**: `ReviewCreated` domain event → singleton `Channel<Guid>` → **`ReviewSentimentHostedService`** (modeled on `CartExpirySweeper`) that scores tracked reviews (`SentimentScore`/`SentimentLabel`/`ProcessedAt`); plus a **slow-cycle re-scan** of `ProcessedAt IS NULL` as the restart-safety / retry net (REQUIREMENTS §6.3).
- **Admin sentiment dashboard**: `GET /analytics/sentiment-summary` + `GET /analytics/products-needing-attention` (StoreManager + Administrator); a sentiment tile + Products-Needing-Attention panel on the admin home.
- **One new RBAC policy** `Sentiment.View` (StoreManager + Administrator) — see §3.4 / §12.
- **Hermetic tests**: `FakeLlmClient` + `FakeTextAnalyticsAdapter` via `ApiFactory.ConfigureTestServices`; review-flow / copy-gen / sentiment-scoring / dashboard integration tests + unit tests with `Mock<ILlmClient>`.
- **One accessible primitive**: `RatingStars` (RHF-compatible, radio-group semantics) → keeps the hand-built component story honest.
- **Dev-only seeder**: ~10 reviews with varied sentiment so the storefront list + sentiment tile + Needing-Attention panel show real data on first run.

**Out / deferred:**
- **AI chatbot** (REQUIREMENTS §7) + **demand forecasting** + **order anomaly** (§9/§10) → **Phase 5**. Phase 4 builds *only* copy-gen + reviews + sentiment, but lands the `ILlmClient` seam the chatbot reuses.
- **OpenAI provider** (`OpenAiLlmClient`) → Phase 6/7 stretch (ADR-0005); Phase 4 ships **Anthropic only** behind the interface.
- **Live key provisioning** (real Anthropic key + Azure AI Language F0 resource) → **deferred follow-up** (user decision, §3.2). Phase 4 ships everything stub-first; going live is a config flip.
- **Guest reviews** → never for this schema (§3.3 — `CustomerProfileId` is NOT NULL by design).
- **Review moderation / pre-publish workflow / editing / helpful-votes / replies** → out (no `Status`/`Title`/`HelpfulCount` columns exist; reviews are visible immediately).
- **Distributed sentiment queue** (Service Bus / leader election) → **Phase 8** event-driven migration; Phase 4's `Channel<Guid>` is in-process (the slow-cycle re-scan covers single-instance restart safety; see §8 + §18).
- **Adding `Review` to the audit-trail allowlist** → no (high-volume; CODING_STANDARDS). Admin copy edits are already captured via the `Product` `Update` audit row.

## 3. Key decisions (2026-06-18)

### 3.1 Full slice, sequenced stub-first (user-confirmed)

**Decision:** build all four sub-features (Reviews + CopyGen + Sentiment +
admin dashboard), in the chunk order C0→C1→{C2 ∥ C3}→C4. **Why:** PLAN Phase 4
(Weeks 9–10) + REQUIREMENTS Epic 4 (Stories 4.1/4.2/4.3) co-scope all of them.
PLAN risk #6 names *AI scope creep* as the dominant risk and the rule is "if it
doesn't trace to a bullet, push back" — so **CopyGen is the explicit cut line**
if the phase overruns (it traces to **no** resume bullet; PLAN §7 calls the AI
features "recruiter hooks, not on either bullet"). Stub-first means every piece
is independently demoable/testable, so a cut is clean.

### 3.2 Stub-first; live providers deferred to a config-flip follow-up (user-confirmed)

**Decision:** implement both the live `AnthropicLlmClient` and the live
`AzureTextAnalyticsClient`, but **default `Ai:Mode=stub` everywhere** (dev, CI,
tests, demo) and **defer provisioning** the real Anthropic key + Azure AI
Language F0 resource to a later follow-up. **Why:** ADR-0005 already locks
Anthropic as the sole shipping provider and `Ai:Mode=live|stub` as the outage
mitigation; stub-first keeps the build hermetic and $0, and — per the saved
"no LLM API claim" résumé rule — CopyGen carries portfolio value only, so we
don't gate the phase on live spend. Going live later = set `Ai:Mode=live` +
two secrets, **no code change** (§16, §17).

### 3.3 Reviews = member-only AND purchase-verified

**Decision:** `POST /products/{id}/reviews` requires `[Authorize(Roles =
Customer)]` **and** a purchase-existence check in `ReviewService`
(non-cancelled order line referencing a variant of the product) → else
`ConflictException` (already-reviewed) / `ValidationException` (not purchased).
**Why:** DATABASE_DESIGN §3.15 makes `CustomerProfileId` **NOT NULL** with **no
guest column** and a unique filtered `UX_Review_ProductId_CustomerProfileId`
(one review per customer per product) — the schema *physically forbids* guest
reviews and enforces single-review. REQUIREMENTS §6.1 says "仅在购买并完成订单后
可对该商品评论". Several map agents drifted toward `GuestEmail`/`ReviewerType` —
**rejected** as contradicting the locked design (§4).

### 3.4 Sentiment dashboard = StoreManager + Administrator (new `Sentiment.View` policy)

**Decision:** gate `sentiment-summary` + `products-needing-attention` with a
**new** policy `Sentiment.View` = `{StoreManager, Administrator}`; gate
`generate-copy` with the existing `Catalog.Manage` (Administrator-only).
**Why:** the REQUIREMENTS §核心模块 matrix (line 35) puts 评论系统/情感汇总 at
**Staff ❌, StoreManager ✅, Administrator ✅** — so the general `Reports.View`
(Staff+, used by sales-by-day) would over-grant. CopyGen (line 37) is
**Administrator-only** ("触发/审核/保存"), which `Catalog.Manage`
(Administrator-only today) matches exactly. This is the one new policy the phase
adds; everything else reuses the Phase-3 set. *(A map agent recommended
`Reports.View` for sentiment — corrected here against the matrix; §4.)*

### 3.5 Sentiment trigger = event-driven `Channel<Guid>`, with a slow re-scan net

**Decision:** primary path is **event-driven** — review insert raises a
`ReviewCreated` domain event whose handler writes the review `Guid` into a
**singleton `Channel<Guid>`** the hosted service consumes (REQUIREMENTS §6.3).
The **same** hosted service *also* runs a slow `PeriodicTimer` tick that
re-scans `WHERE ProcessedAt IS NULL` and re-enqueues — the retry path for Azure
failures **and** the restart-safety net for the in-process channel (which is
lost on restart). **Why:** REQUIREMENTS §6.3 calls for both ("持续失败则保留
`ProcessedAt=null`，由慢周期 sweeper 重试"); the fast channel gives near-real-time
scoring, the slow scan guarantees no review is silently stranded. The
distributed (Service Bus) version is Phase 8.

### 3.6 Sentiment write-back = tracked load + `SaveChanges` (not `ExecuteUpdate`)

**Decision:** `ReviewSentimentService` loads the `Review` **tracked**, sets the
three columns, and `SaveChangesAsync`. **Why:** the Phase-3 audit caveat
(§3.2 of that doc) — set-based `ExecuteUpdateAsync` bypasses the interceptors,
including the `AuditingInterceptor` column stamps. A tracked write keeps
`UpdatedAt`/`UpdatedBy` correct (actor = `"system"`). `Review` is **not** on the
audit-trail allowlist, so no `AuditLog` row is written (intended — high volume).

## 4. Doc-vs-code drifts this phase fixes (recon-verified)

| # | Doc / map claims | Reality → Phase 4 action |
|---|---|---|
| 1 | DATABASE_DESIGN §5: migration is `0004_reviews_sentiment` | The on-disk sequence is monotonic; last applied is `20260617095311_0008_shipment_audit`. **Physical file = `<ts>_0009_reviews_sentiment`**; keep "0004" only as the design-table label. (`0004` is already taken by `0004_orders`.) |
| 2 | Map agents proposed `GuestEmail` / `ReviewerType` / `CK_Review_Identity` XOR | Contradicts §3.15 (`CustomerProfileId` NOT NULL, no guest column). **Member-only; no guest fields.** (§3.3) |
| 3 | Map agents assumed `SentimentScore` 0..1 and "Needing Attention < 0.3" | Authoritative range is `decimal(4,3)` in **−1..1** (= PositiveScore − NegativeScore); threshold is avg **< −0.2** (REQUIREMENTS §6.4). |
| 4 | Map agents invented `Title` / `Status` / `IsPublished` / `HelpfulCount` | None exist in §3.15. Columns are exactly: `Id, ProductId, CustomerProfileId, Rating, Body, SentimentScore?, SentimentLabel?, ProcessedAt?, IsDeleted` (+ `IAuditableEntity` audit columns). Reviews are visible immediately. |
| 5 | A map agent framed Azure OpenAI vs Anthropic as open | **Settled** by ADR-0005 (Accepted) + REQUIREMENTS Task 4.2.1: Anthropic Claude via `Anthropic.SDK` behind `ILlmClient`; OpenAI is Phase 6/7 stretch. |
| 6 | A map agent recommended `Reports.View` (Staff+) for sentiment | The matrix excludes **Staff** from 情感汇总 → new `Sentiment.View` (SM+Admin). (§3.4) |
| 7 | Map implied some LLM wiring exists | Verified: **`Retail.Api/Ai/` exists but is an empty `.gitkeep` placeholder; no `ILlmClient`/provider code in src** (only an `Anthropic` comment in `Program.cs`). C2 builds the seam greenfield per ADR-0005 / CODING_STANDARDS § AI Client 抽象. |
| 8 | `IX_Review_ProductId_CreatedAt` references a `CreatedAt` column not in the §3.15 column list | `CreatedAt` is supplied by `IAuditableEntity` (stamped by `AuditingInterceptor`), same as every other entity. The index is on `(ProductId, CreatedAt)`. |

## 5. Data model — migration `0009_reviews_sentiment`

### 5.1 `SentimentLabel` enum (`Common/Enums/CommerceStatuses.cs`)

`: byte` → `tinyint`, nullable on the entity (unscored until processed),
following the established 1-based convention beside `OrderStatus`/`ShipmentStatus`:
```
SentimentLabel { Positive = 1, Neutral = 2, Negative = 3, Mixed = 4 }
```

### 5.2 `Review` entity (`IAuditableEntity`; child of `Product` + `CustomerProfile`)

Exactly DATABASE_DESIGN §3.15 — **no** `Title`/`Status`/`HelpfulCount`/guest fields:
- `Id` Guid PK (`newsequentialid()`); `ProductId` Guid FK **NOT NULL** (`Cascade` from Product); `CustomerProfileId` Guid FK **NOT NULL** (`Restrict`/`NoAction` — no cascade path); `Rating` `tinyint` (CHECK 1..5); `Body` `nvarchar(4000)`; `SentimentScore` `decimal(4,3)?` (−1..1); `SentimentLabel` `tinyint?`; `ProcessedAt` `datetime2(7)?`; `IsDeleted` `bit` default 0; audit columns from `IAuditableEntity`.
- **Indexes:** `IX_Review_ProductId_CreatedAt`, `UX_Review_ProductId_CustomerProfileId` (**unique, filtered `IsDeleted = 0`** → one live review per customer per product).
- **Constraint:** `CHECK (Rating BETWEEN 1 AND 5)`.
- **Global query filter:** `IsDeleted = false` (alongside `Product`/`Category`).
- `Product` gains `ICollection<Review> Reviews` (currently only `Variants` + `Images`).

EF config = `Data/Configurations/ReviewConfiguration.cs : IEntityTypeConfiguration<Review>`
(auto-discovered via `ApplyConfigurationsFromAssembly`; model on `ShipmentConfiguration`).
`RetailDbContext` gains `public DbSet<Review> Reviews => Set<Review>();`. `Review`
is **not** added to the `AuditTrailInterceptor` allowlist.

## 6. AI client abstraction — `ILlmClient` (ADR-0005, CODING_STANDARDS § AI Client 抽象)

Greenfield in `Retail.Api/Ai/`:
- **Contracts** (`Ai/Contracts/`, our own records — never SDK types): `LlmRequest`, `LlmCompletion`, `LlmMessage`, `LlmRole`, `LlmTool`, `LlmToolUse`, `LlmToolResult`, `LlmToolChoice`, `LlmUsage` (cross-provider LCD shape: messages + tools + tool_choice + usage — full record set per CODING_STANDARDS § AI Client 抽象; `LlmRequest.Messages` is `IReadOnlyList<LlmMessage>`).
- **Interface** `Ai/ILlmClient.cs`: `Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken ct)`.
- **Providers** (`Ai/Providers/`): `AnthropicLlmClient` (live — `Anthropic.SDK` via `IHttpClientFactory` + `AddStandardResilienceHandler()`; maps `LlmToolChoice.RequiredTool("emit_product_copy")` → Anthropic `{"type":"tool","name":"emit_product_copy"}`; transport errors → `ExternalServiceException` → 503); `StubLlmClient` (canned fixtures from `tests/fixtures/ai/`).
- **Options** `Ai/AiSettings.cs` (`IOptions<AiSettings>`, section `"Ai"` — canonical name per CODING_STANDARDS / ADR-0005): `Mode` (`live|stub`), `Provider` (`anthropic`), `ApiKey`, `Models:Copy` (logical→real model-id map). Follows the `StripeOptions` pattern: blank `appsettings.json` placeholder, dev value via user-secrets, `ApiFactory` in-memory override + `Ai:Mode=stub`; conditional `ValidateOnStart` **outside Development only**.
- **DI** (`Program.cs`, near the hosted-service registrations): a **single** `ILlmClient` binding resolved by `Ai:Mode` (`stub` → `StubLlmClient`, else `AnthropicLlmClient`). Business services see only `ILlmClient`.
- **Compile-time guard:** `Directory.Build.targets` rule forbids `using Anthropic.SDK` outside `Ai/Providers/`.

## 7. CopyGen — `POST /catalog/products/{id}/generate-copy` (Administrator-only)

`ICopyGenService` / `CopyGenService` (depends on `ILlmClient` only):
- Loads the product (name, category, brand, variant options). **In-context examples:** Phase 4 uses **static few-shot examples baked into the system prompt** — matching the CODING_STANDARDS reference `CopyGenService` (which has no similar-products step). REQUIREMENTS §8.1's "pick ~2 similar products' descriptions" *dynamic retrieval* is a **deferred enhancement** (it needs a new `IProductRepository` same-category query and a similarity rule, not worth it on the cut-line feature). Decision recorded so it isn't a silent drop; if revisited, the rule is: same-category → fallback same-brand → oldest 2 with non-empty `Description`, omit if < 2.
- Builds the prompt + the `emit_product_copy` tool; forces it via `LlmToolChoice.RequiredTool` for guaranteed structured JSON.
- Returns `SuggestProductCopyResponse { description, seoTitle, seoMetaDescription, bulletPoints[] }`. **Never persists** — the admin accepts into the description field, which saves via the existing `PUT` catalog update.
- Request `SuggestDescriptionRequest { tone (playful|professional|luxury), length (short|medium|long) }` + validator.
- `[Authorize(Policy = Roles.Policies.CatalogManage)]`. External 5xx → `ExternalServiceException` → 503 `EXTERNAL_SERVICE_UNAVAILABLE` (ExceptionMiddleware arm).

## 8. Sentiment pipeline — `ITextAnalyticsAdapter` + `ReviewSentimentHostedService`

- **Abstraction** `Ai/ITextAnalyticsAdapter.cs` (Adapter suffix per CODING_STANDARDS §97 / REQUIREMENTS Task 4.3.1): `Task<SentimentResult> AnalyzeAsync(string text, CancellationToken ct)` → `SentimentResult { Score (−1..1), Label }`. Providers: `AzureTextAnalyticsAdapter` (`Azure.AI.TextAnalytics`, F0; maps Azure's per-class confidence to `PositiveScore − NegativeScore` and `Positive/Neutral/Negative/Mixed` → `SentimentLabel`; **transient failures retry via Polly** — `AddStandardResilienceHandler` on the adapter's `HttpClient`, per REQUIREMENTS §6.3 "Polly 重试") + `StubTextAnalyticsAdapter` (deterministic keyword/fixture scoring). `AzureAiLanguageOptions { Endpoint, ApiKey, Enabled }` — conditional `ValidateOnStart` **outside Development only** (mirrors `StripeOptions`; stub/dev/test/CI boot needs no keys). On persistent failure the review keeps `ProcessedAt = null` for the slow scan (§3.5).
- **Domain event** `ReviewCreated(Guid ReviewId)` raised by `ReviewService` after insert; its handler enqueues the id into a **singleton `Channel<Guid>`** (registered in `Program.cs`).
- **`HostedServices/ReviewSentimentHostedService : BackgroundService`** — the exact `CartExpirySweeper` shape (`IServiceScopeFactory`, `TimeProvider`, `ILogger`; per-tick `CreateAsyncScope`; per-tick try/catch that logs and continues; outer `OperationCanceledException`). Consumes the channel as the fast path; a slow `PeriodicTimer` tick re-scans `ProcessedAt IS NULL` (retry + restart-safety). `AddHostedService` beside `CartExpirySweeper`.
- **`IReviewSentimentService` / `ReviewSentimentService`** (scoped, mirrors `ICartSweepService`): tracked-load the `Review`, call `ITextAnalyticsAdapter`, set `SentimentScore`/`SentimentLabel`/`ProcessedAt`, `SaveChangesAsync` (§3.6). Per-review try/catch — a failure leaves `ProcessedAt = null` for the slow scan and never kills the loop.

## 9. Admin sentiment dashboard

- `IReportQueryService` extension `GetSentimentSummaryAsync` (mirrors the Phase-3 `GetSalesByDayAsync` in-memory `GroupBy`): per-product average `SentimentScore` + label distribution + review count.
- `GetProductsNeedingAttentionAsync`: products whose average `SentimentScore < −0.2` (REQUIREMENTS §6.4), ordered worst-first.
- `AnalyticsController` (existing): `GET /api/v1/analytics/sentiment-summary` + `GET /api/v1/analytics/products-needing-attention`, both `[Authorize(Policy = Roles.Policies.SentimentView)]`.
- FE: a `SentimentMetricsTile` + `ProductsNeedingAttention` panel on the admin home, gated by `ROLE_SETS` (SM+Admin); Recharts label-distribution + `Badge` color-coding; `EmptyState` until reviews are scored.

## 10. Customer reviews (storefront)

- `POST /api/v1/products/{id}/reviews` `[Authorize(Roles = Customer)]` → 201; `GET /api/v1/products/{id}/reviews` `[AllowAnonymous]` paged → `ApiResponse<ReviewListDto>` where `ReviewListDto { Page: PagedResult<ReviewDto>; Summary: ReviewSummary }` and `ReviewSummary { Average, Count, Distribution[1..5] }` — the **average + rating-distribution** computed over ALL non-deleted reviews for the product, not just the current page (REQUIREMENTS §6.2 "平均评分 + 评分分布柱状图"). (New `ReviewsController`, or `CatalogController` extension.)
- `ReviewService.SubmitReviewAsync`: product exists → verify purchase → not-already-reviewed (`UX_Review` unique; dup → 409) → insert → raise `ReviewCreated`.
- `IReviewRepository` (Scoped): `AddAsync`, `ListByProductIdAsync` (paged `AsNoTracking`), `ExistsForCustomerAndProductAsync`, `GetPendingSentimentAsync` (the slow-scan query), `SaveChangesAsync`.
- DTOs: `SubmitReviewRequest { Rating, Body }` + validator (`Rating` 1..5, `Body` NotEmpty MaxLength 4000); `ReviewDto { Id, CustomerName, Rating, Body, SentimentScore?, SentimentLabel?, CreatedAt }`; `ReviewMappers`.
- FE: `features/storefront/hooks/useReviewsQuery.ts` + `useReviewMutations.ts` (key factory, CSRF auto); `RatingDistributionChart` (average + Recharts bars over `Summary.Distribution[1..5]`, REQUIREMENTS §6.2) above `ReviewsList` + `ReviewSubmitForm` (zod schema mirroring the validator), mounted in `ProductDetailPage.tsx` after `product.description`; new `components/ui/rating-stars.tsx` (`forwardRef`, radio-group semantics — accessible, RHF-compatible).

## 11. API surface (new)

```
ReviewsController     POST /api/v1/products/{id}/reviews              (Roles=Customer)        {rating, body} → 201
                      GET  /api/v1/products/{id}/reviews              (AllowAnonymous)        paged + {average, count, 1..5 distribution}
CatalogController     POST /api/v1/catalog/products/{id}/generate-copy (Catalog.Manage)       {tone, length} → suggested copy (no save)
AnalyticsController   GET  /api/v1/analytics/sentiment-summary        (Sentiment.View)        per-product avg + label dist
                      GET  /api/v1/analytics/products-needing-attention (Sentiment.View)      avg < −0.2, worst-first
```
All return the standard `ApiResponse<T>` envelope; lists ride `PagedResult<T>`;
query DTOs follow the `[FromQuery]` PascalCase convention.

## 12. Authorization design

- **New policy** `Sentiment.View` = `RequireRole(StoreManager, Administrator)` added to the single `AddAuthorization` block; constant `Roles.Policies.SentimentView = "Sentiment.View"`.
- **Reuse:** `generate-copy` → `Catalog.Manage` (Administrator-only, matches the AI-copy matrix row); review **submit** → `[Authorize(Roles = Customer)]`; review **list** → `[AllowAnonymous]`.
- **Not authorization:** the purchase-verified + single-review rules are domain checks in `ReviewService` (→ 422 / 409), not policies.

## 13. Environment & secrets

- **Default everywhere** (dev / CI / tests / demo): `Ai:Mode=stub` — no keys, $0, deterministic.
- **Live (deferred follow-up):** `Ai:Mode=live` + `Ai:ApiKey` (Anthropic `sk-ant-…`) + `Ai:AzureLanguage:Endpoint` / `Ai:AzureLanguage:ApiKey` (Azure AI Language F0 resource in `australiaeast`). Documented in `.env.example` + user-secrets; `ValidateOnStart` for the live keys applies **outside Development only**, so dev/test/CI never break on a missing key.
- `ApiFactory` injects `Ai:Mode=stub` + registers `FakeLlmClient` / `FakeTextAnalyticsAdapter` via `ConfigureTestServices` (the Stripe-fake pattern) so integration tests never touch the network.

## 14. Testing & E2E plan

- **`ReviewFlowTests`:** submit as Customer → 201; Anonymous → 401; `Rating` out of 1..5 → 422; `Body` > 4000 → 422; duplicate → 409; **not-purchased → 422** (write this path first); list as Anonymous paged + ordering.
- **`CopyGenTests`:** Admin → 200 (stub fixture); Staff/StoreManager → 403; unknown product → 404; validation → 422; asserts the tool-forced JSON shape; never persists.
- **`ReviewSentimentTests`:** enqueue a review → the hosted service populates `SentimentScore`/`SentimentLabel`/`ProcessedAt`; a `FakeTextAnalyticsAdapter` failure leaves `ProcessedAt = null` and the loop survives; the slow scan re-picks an unprocessed review; idempotent re-run. Drive the timer via the injected `TimeProvider`.
- **`SentimentDashboardTests`:** `Sentiment.View` gate (Staff → 403, SM/Admin → 200); aggregate average + label distribution; the `< −0.2` filter.
- **Unit:** `ReviewServiceTests`, `CopyGenServiceTests` (`Mock<ILlmClient>`), Azure→`SentimentResult` mapping (fixed Azure-shaped inputs → score/label), `SentimentLabel` enum mapping.
- **Vitest:** `RatingStars` (keyboard + ARIA), `ReviewSubmitForm` validation, `RatingDistributionChart` (aggregates 1–5 counts + average from a fixture), a sentiment-tile render.
- **Playwright:** extend the storefront golden path with a review submit + assert it lists (API-mocked); the admin *Suggest Description* flow (stub copy → Accept fills the field). `@axe-core` scan of the review section.
- **CI:** runs entirely in stub mode — no new secrets; the Coverlet 85% gate (Phase 3) continues; new tests feed the coverage/`100+ tests` numbers (current baseline 191 backend + 17 vitest + 7 e2e).

## 15. Chunking (each independently buildable + verifiable)

- **C0 — Data model.** `SentimentLabel` enum; `Review` entity + `ReviewConfiguration` + `Product.Reviews` nav + `DbSet` + soft-delete filter; migration `0009_reviews_sentiment`. *Verify:* build 0/0, migration applies, CHECK + unique-filtered index present.
- **C1 — Customer reviews (API + storefront).** DTOs/validator/mapper (incl. `ReviewSummary` aggregate); `IReviewRepository`; `ReviewService` (purchase-verify + dup-guard, raises `ReviewCreated` to a no-op channel for now); `ReviewsController` (list returns page + summary); FE hooks + `RatingDistributionChart` + `ReviewsList`/`ReviewSubmitForm` + `RatingStars`, mounted in `ProductDetailPage`. *Verify:* submit→list round-trip; not-purchased→422; dup→409; anonymous list; average + distribution render.
- **C2 — CopyGen (`ILlmClient` + admin button).** `Ai/Contracts` + `ILlmClient` + `AnthropicLlmClient` + `StubLlmClient` + `AiOptions` + DI + compile-guard; `CopyGenService`; `generate-copy` endpoint; FE *Suggest Description* button + tone/length modal + diff/Accept-Reject in `ProductForm`. *Verify (stub):* Admin → copy JSON; Staff → 403; Accept writes via PUT; never auto-saves.
- **C3 — Sentiment (hosted service + dashboard).** `ITextAnalyticsAdapter` + Azure/stub providers + options; singleton `Channel<Guid>` + `ReviewCreated` handler; `ReviewSentimentHostedService` (channel + slow scan) + `ReviewSentimentService`; `GetSentimentSummaryAsync`/`GetProductsNeedingAttentionAsync` + endpoints (`Sentiment.View`); FE sentiment tile + Needing-Attention panel. *Verify:* enqueue → scored; failure→`null`→slow-scan retry; dashboard aggregates + `< −0.2`.
- **C4 — Tests, fixtures, seed, docs.** `FakeLlmClient`/`FakeTextAnalyticsAdapter`; the full test set (§14); dev-only ~10-review seeder; `.env.example`/user-secrets docs; reconcile DATABASE_DESIGN/PLAN drifts (§4); fold the `0009` migration-number note back into DATABASE_DESIGN §5 at the docs pass. *Verify:* all green in stub-mode CI; gate holds.

## 16. Resume-bullet alignment

Verified against `project_resume_targets.md`: **none** of the 9 target bullets
name an LLM or CopyGen — PLAN §7/§15 classify the AI features as "recruiter
hooks, not on either bullet," and the saved **"no LLM API claim"** rule means
CopyGen is portfolio/learning only and must not be over-invested. Where Phase 4
*does* reinforce bullets:
- **Azure / platform (Job A-1):** the **Azure AI Language** integration via `ITextAnalyticsAdapter` + `AzureTextAnalyticsClient` is a clean *managed-ML service-call* artifact that survives interview drill-down precisely because it is **not** a model-training claim — the better résumé-bearing AI piece than CopyGen.
- **Backend / REST + EF (Job A-2 / B-3):** the `Review` entity + repository (paged `AsNoTracking` + filtered unique index) + the new `/reviews` + `/generate-copy` + `/analytics/sentiment-*` endpoints add to the REST/EF/pagination surface, all OpenAPI-documented.
- **Async / event-driven (Job A-3 precursor):** `ReviewSentimentHostedService` consuming a `Channel<Guid>` fed by `ReviewCreated` is the in-process precursor migrated to Service Bus + Functions in Phase 8 (where A-3's "10K+ events/day, 70% sync reduction" is actually measured).
- **Testing / CI (Job A-4 / B-4):** the new integration + `Mock<ILlmClient>` unit tests feed the "85% coverage / 100+ tests / xUnit + Moq / CI on every PR" numbers.
- **Frontend components (Job B-1):** the accessible `RatingStars` + reviews list/form + sentiment tile add to the "12+ reusable accessible components" count.

Net: build the full slice, but the résumé payoff concentrates in the
**Azure-sentiment + REST/EF + testing** seams; **CopyGen is the demo flourish
and the first thing to cut** if the two-week phase is at risk.

## 17. Open items / follow-ups

- **Live-provider provisioning** (real Anthropic key + Azure AI Language F0 resource) — deferred to a config-flip follow-up (§3.2); the build is live-ready behind `Ai:Mode`.
- **OpenAI provider** (`OpenAiLlmClient`) — Phase 6/7 stretch (ADR-0005); the interface lands now so it's a ~1-day add later.
- **Distributed sentiment queue** (Service Bus / leader election; multi-instance double-scoring & restart durability) — Phase 8. The in-process `Channel<Guid>` + slow re-scan is the single-instance answer for now (§18).
- **Chatbot + forecasting + anomaly** (the other AI features) — Phase 5, reusing the `ILlmClient` seam.
- **One-line `DATABASE_DESIGN.md` correction** (Review migration is `0009`, not `0004`) at the next docs pass.
- **Review moderation / editing / helpful-votes** — out of scope; revisit only if a bullet needs it.

## 18. Known limitations (in-process phase)

- **Restart durability:** the `Channel<Guid>` is in-process; a crash between enqueue and scoring loses the signal. **Mitigation in scope:** the slow `ProcessedAt IS NULL` re-scan re-picks stranded reviews (§3.5). The durable fix is Service Bus (Phase 8).
- **Multi-instance:** like `CartExpirySweeper`, the hosted service runs on every instance with no leader election; with an in-process channel each instance only scores what it enqueued, and the slow scan can double-attempt across instances. Acceptable at portfolio scale (scoring is idempotent on `ProcessedAt`); the real fix is the Phase-8 queue.
- **Azure F0 limits:** 5k tx/month + rate limits; a burst or retry storm can 429. **Mitigation:** per-review try/catch, throttle → `ExternalServiceException`, leave `ProcessedAt = null` for the slow retry, small batch cap.
