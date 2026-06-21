# Phase 5B — Order-Anomaly Detection (Z-score): Implementation Scope

> Authoritative pre-build scope for **Phase 5B, part 1 — order-anomaly detection**
> (Epic 5, `PLAN.md:519` §8d; REQUIREMENTS §10; ADR-0003). Phase 5B is **split**
> into **anomaly first** (this doc) and **demand forecasting** (a later companion
> `PHASE_5B_FORECAST_SCOPE.md`) — see §3.1. Where this doc disagrees with
> `PLAN.md` / `REQUIREMENTS.md` / `DATABASE_DESIGN.md` / `ADR-0003`, **this doc
> wins for the phase** — deltas are in §3 (decisions) and §4 (drift). Companion to
> `PHASE_2_SCOPE.md` / `PHASE_3_SCOPE.md` / `PHASE_4_SCOPE.md` / `PHASE_5A_SCOPE.md`.
> Source of truth for the phase.

## 1. Goal & demo target

Phase 5A shipped the chatbot. Phase 5B adds the project's **ML/analytics** surface;
its first half is **order-anomaly detection** — an in-process, fully **$0/keyless**
feature (pure Z-score math, no Azure, no API key, unlike the Phase-4 sentiment
adapter). It is also the cleanest **async/event-driven** artifact in the project: a
recurring `BackgroundService` that scans orders and feeds a back-office risk queue —
the in-process precursor that Phase 8 migrates to an `OrderAnomalyScanFn` Function.

**The flow (REQUIREMENTS §10):** every 15 minutes an `OrderAnomalyHostedService`
scans recent paid orders and applies **three rules** (any hit → flag): (1) a
**Z-score** on the customer's recent order totals, (2) a **new shipping country**
never seen on that customer's prior orders, (3) a **quantity** spike (any line > 5).
A flagged order gets an `OrderAnomaly` row; the back-office **Risk Queue** lists
unacknowledged anomalies; and a **flagged, unacknowledged order is blocked from
Mark-Shipped** until a Staff/StoreManager **acknowledges** it.

**Demo (acceptance bar, `PLAN.md` §8d / REQUIREMENTS §10):** a Development-only
**6-month synthetic order seeder** (deterministic) lands a realistic order history
that includes a few deliberately anomalous orders (a huge total, a never-seen
country, a 9-of-one-variant line). On a fresh dev run the anomaly scan flags those
orders → they appear in the **Risk Queue** (`/admin/risk`); attempting **Mark
Shipped** on one returns a blocking error until **Acknowledge** is clicked, after
which the order ships normally.

## 2. Scope boundary

**In:**
- **`OrderAnomaly`** entity (DATABASE_DESIGN §3.19) + migration **`0011_order_anomaly`** (anomaly table only — see §3.1 / §4).
- **`ZScoreScorer`** in `Retail.Ml/Anomaly/` (pure C#, no EF, no ML.NET package) — gives `Retail.Ml` its first real code, with the ADR-0003 numerical-safety guards (σ==0 / σ<1e-6 → not-anomalous) and deterministic, side-effect-free math.
- **`IOrderAnomalyService` / `OrderAnomalyService`** (scoped) — the 3-rule evaluator over recent orders; idempotent (never double-flags an order).
- **`OrderAnomalyHostedService` : BackgroundService** — 15-min `PeriodicTimer` (injected `TimeProvider`), scoped scorer per tick, log-and-continue — modeled exactly on `CartExpirySweeper` / `ReviewSentimentHostedService`.
- **Risk Queue read + Acknowledge write**: `GET /api/v1/analytics/anomalies` (paged, unacknowledged-first) + `POST /api/v1/analytics/anomalies/{id}/acknowledge`, both gated by a **new `Anomaly.Manage` policy** (Staff + StoreManager + Administrator — §3.6 / §12).
- **Mark-Shipped block**: the existing Phase-3 fulfilment ship path rejects an order that has an **unacknowledged** `OrderAnomaly` — **evaluating the order on the spot** if the 15-min scan hasn't reached it yet, so the block can't be timing-bypassed (a guarded, tested edit into shipped code — §3.5).
- **Development-only 6-month synthetic order seeder** (`OrderDemoSeeder`) — deterministic (seeded `Random`), seasonality + weekly cycle + trend, with a handful of injected anomalies; feeds the anomaly demo now and demand-forecasting later (§3.7).
- **Storefront/admin FE**: an `/admin/risk` Risk Queue page (DataTable + Acknowledge action) + `SidebarNav` item + `ROLE_SETS.risk`; an anomaly badge on the admin order workbench.
- **Hermetic tests** (no Azure, no key): scorer unit tests (each rule + the σ-guard), anomaly-scan + ship-block + RBAC integration tests, Risk Queue Vitest.
- **ADR-0003 amendment** reconciling the per-customer-baseline design to the shipped PLAN-§8d batch-scan (§3.2 / §4).

**Out / deferred:**
- **Demand forecasting** (ML.NET SSA, `ml-train.yml`, `DemandForecast`/`ReorderHint`) → **Phase 5B part 2** (`PHASE_5B_FORECAST_SCOPE.md`); migration `0012_demand_forecast`.
- **`OrderAnomalyScanFn` Azure Function** → Phase 8 (PLAN §13). 5B ships the in-process `BackgroundService`; Phase 8 measures the async/event-driven résumé numbers.
- **`CustomerSpendingBaseline` table + nightly baseline rebuild** (ADR-0003's heavier fraud design) → not built; the scan computes the per-customer mean/σ on the fly from recent orders (§3.2).
- **A persisted scan watermark / `EvaluatedAt` column** → out; the scan re-evaluates a bounded recent window each tick and skips already-flagged orders (§3.4).
- **Per-rule rows / multi-anomaly per order** → out; **one `OrderAnomaly` row per flagged order**, `Reason` carries the combined human-readable cause(s) (§3.3).
- **ML-based / Azure Anomaly Detector** → never (retired; ADR-0003). Z-score only.

## 3. Key decisions (2026-06-21)

### 3.1 Phase 5B split: anomaly first, forecasting second (user-confirmed)

**Decision:** build + review **order-anomaly** as its own sub-phase (this doc),
then **demand forecasting** (ML.NET SSA) as a separate follow-on. **Consequence:**
the combined `0005_chat_forecast_anomaly` design label (already superseded by 5A's
`0010_chat_sessions`) splits again — anomaly ships `0011_order_anomaly` (this
phase); forecasting ships `0012_demand_forecast` (next). **Why:** anomaly is pure,
$0, in-process math with no new package risk — a fast, fully-demoable win that lands
the async/event-driven story; forecasting is the ML.NET-heavy, newest-to-the-author
piece (packaging, trainer CLI, `ml-train.yml`), best isolated and reviewed on its own.

### 3.2 Anomaly engine = PLAN-§8d batch-scan; ADR-0003 amended (not the baseline table)

**Decision:** ship the **REQUIREMENTS §10 / PLAN §8d** design — a 15-min
`OrderAnomalyHostedService` that computes each customer's mean/σ **on the fly** from
their recent orders and applies the 3 rules — and **amend ADR-0003** to record this
as-built. **Why:** ADR-0003 sketched a heavier checkout-inline fraud scorer with a
`CustomerSpendingBaseline` table + nightly rebuild, but the `OrderAnomaly` entity,
PLAN §8d, and the Phase-8 `OrderAnomalyScanFn` all assume the lightweight batch-scan.
The on-the-fly computation is simpler, needs no extra table, and is plenty at
portfolio scale; the baseline-table optimization is a documented future option.

### 3.3 One `OrderAnomaly` row per flagged order

**Decision:** a flagged order gets **exactly one** `OrderAnomaly` row.
`Score` = the Z-score (rule 1) or `0` when only rules 2/3 fire; `Reason` is a
human-readable, possibly-combined string (e.g. *"Order total 4.2σ above this
customer's mean; ships to a country not seen on prior orders"*). **Why:** matches the
§3.19 schema exactly (one `OrderId` FK, one `Score`, one `Reason`, `IX_OrderAnomaly_OrderId`);
the risk queue + ship-block reason about *the order*, not per-rule rows. Keeps the
acknowledge action one-click-per-order.

### 3.4 Idempotent scan = recent window minus already-flagged (no watermark column)

**Decision:** each 15-min tick evaluates **paid orders placed in the last 14 days
that do not already have an `OrderAnomaly` row**. **Why:** the §3.19 schema has no
`EvaluatedAt`/watermark, and adding one is out of scope. Skipping already-flagged
orders makes flagging idempotent; the 14-day window bounds the query; and
re-evaluating *unflagged* recent orders each tick is correct (a customer's mean/σ
shifts as new orders arrive, so an order can legitimately become anomalous later)
and cheap at portfolio scale. Restart-safe (no in-memory state). Known limitation:
an order older than the window is never retro-flagged (acceptable — anomalies matter
pre-fulfilment, which is recent).

### 3.5 Mark-Shipped block = an authoritative evaluate-on-ship guard (user-confirmed)

**Decision:** the existing fulfilment **Mark-Shipped** service, before the
Paid→Fulfilled transition, ensures the order has been evaluated — if it has **no**
`OrderAnomaly` row yet (the 15-min scan hasn't reached it), it calls
`IOrderAnomalyService.EvaluateOrderAsync(orderId)` **synchronously**, then rejects if
an **unacknowledged** anomaly now exists, throwing `ConflictException` (→ 409,
*"This order is flagged for review — acknowledge it in the Risk Queue before
shipping."*). **Why:** REQUIREMENTS §10.2 is explicit, and it's the feature's
point — anomaly detection with no teeth is just a list. A *passive* "does a flag row
exist?" check would be **timing-bypassable**: `MarkShippedAsync` flips Paid→Fulfilled
immediately, so an order placed and shipped inside one 15-min window (exactly the
demo's click-path) would never have been scanned and would ship unchecked.
Evaluate-on-ship makes the guard authoritative regardless of scan cadence; the
hosted service then just pre-populates the risk queue ahead of time. The membership
check uses a dedicated repo query (§7), **not** a navigation collection (the ship
load path doesn't include `Anomalies` — see §7 / drift). It's the only cross-phase
edit this phase makes; regression-tested both ways.

### 3.6 RBAC = new `Anomaly.Manage` policy (Staff + StoreManager + Administrator)

**Decision:** add `Anomaly.Manage = RequireRole(staffPlus)` (constant
`Roles.Policies.AnomalyManage`); it gates **both** the Risk Queue read and the
Acknowledge write. **Why:** the REQUIREMENTS matrix puts *处理风险队列 / 处理订单异常*
at **Staff ✅** (unlike sentiment, which is SM+). A dedicated named policy (mirroring
how `Sentiment.View` / `Chat.View` were added per-feature) keeps the capability
matrix legible. Acknowledge is Staff-capable per the matrix; no SM+ split needed.

### 3.7 Synthetic seeder = Development-only, deterministic, injected anomalies

**Decision:** an `OrderDemoSeeder` (Development-only, idempotent) generates ~6 months
of orders across a few demo customers using a **seeded `Random`** (reproducible) with
weekly/seasonal variation + a mild trend, and **injects a few clear anomalies** (a
~5σ total, a never-seen shipping country, a 9-unit line). **Why:** the anomaly scan
(and later forecasting) are unverifiable/un-demoable against an empty store; a
deterministic seed makes the demo + tests reproducible.

**Direct-insert obligations (the seeder bypasses `OrderCreationService`, so it must
satisfy its invariants itself):** (1) every `OrderLine.ProductVariantId` is a
`Restrict` FK → reuse the **existing seeded catalog** (the product seeder's
`Category → Product → ProductVariant` with `PriceCents`) rather than inventing
variants; (2) `Order` carries a member-XOR-guest **CHECK** (`CustomerProfileId` set
⊕ `GuestEmail` set) — seed members with a `CustomerProfileId` (so rules 1–2 have a
baseline); (3) supply a **monotonic `OrderNumber`** (match the production sequence/
generator, not `Random`); (4) populate the **`ShippingAddressJson`** snapshot
(incl. `Country`) so rule 2 has data; (5) set `Status = Paid`, a `PlacedAt` spread
across 180 days, and consistent `TotalCents` = Σ line price×qty. **Idempotency
guard:** no-op outside Development; key the run on a durable, late-written **sentinel
order** (e.g. a known `OrderNumber`/marker checked *after* the catalog exists), so a
partial-seed failure re-runs cleanly rather than the fragile "demo customers exist"
check. Audit rows it generates are accepted dev-only noise (§17).

## 4. Doc-vs-code drifts this phase fixes (recon-verified)

| # | Doc / spec says | Reality → Phase 5B action |
|---|---|---|
| 1 | DATABASE_DESIGN §5: 5B = `0011_forecast_anomaly` (all 3 tables) | 5B is split (§3.1). Anomaly ships **`0011_order_anomaly`** (OrderAnomaly only); forecasting ships `0012_demand_forecast` later. Update §5 to two rows at the C4 docs pass. |
| 2 | §3.19 `OrderAnomaly` PK `Default = newsequentialid()`, `DetectedAt datetime2(7) sysutcdatetime()` | As-built convention (Phase-2 drift #7 / 5A drift): GUID PKs are **client/EF-generated** (no DB default) and timestamps are **`datetimeoffset`** service-stamped via `TimeProvider`. `DetectedAt` = `DateTimeOffset`. |
| 3 | ADR-0003: per-customer `CustomerSpendingBaseline` table + nightly rebuild + checkout-inline scorer | Superseded by the PLAN-§8d 15-min batch-scan with on-the-fly mean/σ (§3.2). ADR-0003 amended at the C4 docs pass; no baseline table. |
| 4 | REQUIREMENTS §10.1 "uses the customer's last 50 orders; < 5 → global mean" | As-built (no contradiction with §10.1): Rule 1 baseline = the buyer's prior **Paid** orders **excluding the order under test**, keyed on `CustomerProfileId`; **< 5 prior → global mean/σ** (the σ-guard covers a near-empty store, so no extra "globally sparse" threshold). **Guest** orders (no profile/history) also fall to the **global** baseline for rule 1. **Rule 2 requires ≥ 1 prior order** — a first-ever order has no prior countries, so it is never "new-country"-flagged. Rule 3 (qty) applies to all orders. |
| 5 | §3.19 lists only Id/OrderId/Score/Reason/DetectedAt/Acknowledged | The acknowledge actor + time are **not** new columns — `IAuditableEntity.UpdatedBy/UpdatedAt` (interceptor-stamped on the only update an `OrderAnomaly` ever gets) record who-cleared-it + when, matching `ReorderHint.Dismissed` (bare bit) and `Review` (who-touched via `UpdatedBy/At`). No `AcknowledgedBy/At` columns. |
| 6 | §3.19 `OrderAnomaly` is a child of `Order` | FK `OrderId` is **`Cascade`** (matching every other single-FK `Order` child — `OrderLine`/`Payment`/`Shipment`/`OrderPriceBreakdown`), **not** `Restrict`. With one FK there is no multiple-cascade-path collision, and cascade avoids orphan anomaly rows on order teardown. |

## 5. Data model — migration `0011_order_anomaly`

### 5.1 `OrderAnomaly` entity (`IAuditableEntity`; child of `Order`)

Per DATABASE_DESIGN §3.19 (with the as-built type/default conventions of §4 row 2):
- `Id` Guid PK (client-generated, no DB default); `OrderId` Guid **FK NOT NULL** (**`Cascade`** — matches every other single-FK `Order` child; §4 row 6); `Score` `decimal(8,3)` (the Z-score, or 0 when only rules 2/3 fire); `Reason` `nvarchar(200)` **NOT NULL**; `DetectedAt` C# `DateTimeOffset` → SQL **`datetimeoffset`**; `Acknowledged` `bit` default false; audit columns from `IAuditableEntity`. **No `AcknowledgedBy/At` columns** — who-cleared + when ride the interceptor-stamped `UpdatedBy`/`UpdatedAt` (§4 row 5).
- **Indexes:** `IX_OrderAnomaly_OrderId`; `IX_OrderAnomaly_Acknowledged_DetectedAt` (the risk-queue query: unacknowledged, newest first).
- **One-directional FK** (`WithMany()` no back-collection, the `Order → CustomerProfile` style) — no `Order.Anomalies` nav. Both the ship-block check and the scan idempotency run **direct `OrderAnomalies` queries** (§7), not a loaded collection.
- Not soft-deletable; **not** on the `AuditTrailInterceptor` allowlist (high-volume, system-generated — same call as `Review`). The acknowledge mutation is a tracked write, so `UpdatedBy/UpdatedAt` capture the actor + time.

EF config `OrderAnomalyConfiguration : IEntityTypeConfiguration<OrderAnomaly>` (auto-discovered); `RetailDbContext` gains `DbSet<OrderAnomaly> OrderAnomalies`.

## 6. Anomaly engine — `ZScoreScorer` + `OrderAnomalyService`

- **`Retail.Ml/Anomaly/ZScoreScorer.cs`** (pure, no EF/ML.NET): given a value + a sample, returns the Z-score; **σ==0 or σ<1e-6 → 0 (not anomalous)** (ADR-0003 numerical-safety). Per ADR-0003, rule 1 scores on a **log transform** of the amount (`log(TotalCents)`) so the heavy-tailed order-total distribution doesn't mis-fire. Deterministic, unit-testable in isolation — `Retail.Ml`'s first real code, de-risking ML packaging before the forecasting sub-phase needs `Microsoft.ML`.
- **`IOrderAnomalyService` / `OrderAnomalyService`** (scoped): `ScanAsync` selects candidate Paid orders in the recent window without an existing anomaly row (§3.4); `EvaluateOrderAsync(orderId)` scores a single order on demand (the ship-block path, §3.5). Because the shipping address (incl. `Country`) is persisted as a **JSON value-converter column** (`ShippingAddressJson`, *not* a queryable relational column), the per-customer rules are computed **in memory** — load the buyer's recent Paid orders (totals + deserialized countries), then:
  - **Rule 1 (Z-score):** mean/σ of `log(TotalCents)` over the buyer's prior Paid orders (last ~50, **excluding the order under test**) → `ZScoreScorer`; |Z| > 3 flags. **< 5 prior → global** mean/σ (the σ-guard covers a near-empty store). Guests use the global baseline.
  - **Rule 2 (new country):** only when the buyer has **≥ 1 prior order** — flags if the order's shipping country isn't among the prior orders' countries (in-memory compare).
  - **Rule 3 (quantity):** any `OrderLine.Quantity > 5`.
  - Any hit → insert one `OrderAnomaly` (Score = the rule-1 Z, or 0 when only rules 2/3 fire; Reason = combined human-readable cause(s)). `AcknowledgeAsync(id, actor)` flips `Acknowledged` (`UpdatedBy/At` auto-stamped).
- **`OrderAnomalyHostedService` : BackgroundService** — `PeriodicTimer(15 min, _timeProvider)`, `CreateAsyncScope()` per tick → `IOrderAnomalyService.ScanAsync`, per-tick try/catch, `OperationCanceledException` on shutdown. Registered beside the other hosted services (Testing-environment handling in §14).

## 7. Risk Queue + Acknowledge + Mark-Shipped block

- **Read:** `IReportQueryService.GetRiskQueueAsync` (or a dedicated query) → paged unacknowledged `OrderAnomaly` joined to `Order` for context → `AnomalyDto { Id, OrderId, OrderNumber, Score, Reason, DetectedAt, Acknowledged }`. `GET /api/v1/analytics/anomalies` `[Authorize(Policy = AnomalyManage)]`.
- **Acknowledge:** `POST /api/v1/analytics/anomalies/{id}/acknowledge` `[Authorize(Policy = AnomalyManage)]` → tracked load + set `Acknowledged = true` + SaveChanges; the interceptor stamps `UpdatedBy/UpdatedAt` as the actor + time (§4 row 5). 404 if missing.
- **Ship block:** `AdminOrderService.MarkShippedAsync`, *before* the Paid→Fulfilled transition: if the order has no anomaly row yet, call `IOrderAnomalyService.EvaluateOrderAsync` (§3.5); then, via a dedicated **`IOrderRepository.HasUnacknowledgedAnomalyAsync(orderId)`** (`_db.OrderAnomalies.AnyAsync(a => a.OrderId == id && !a.Acknowledged)`), throw `ConflictException` → 409. A nav-collection check would silently no-op — the existing ship load `GetTrackedWithShipmentAsync` includes only `Shipment`, not `Anomalies`. `ConflictException` maps to 409 via `ExceptionMiddleware`. Regression-tested both ways (flagged → 409; acknowledge → ships).

## 8. API surface (new)

```
AnalyticsController  GET  /api/v1/analytics/anomalies                 (Anomaly.Manage)  paged unacknowledged risk queue
                     POST /api/v1/analytics/anomalies/{id}/acknowledge (Anomaly.Manage)  → acknowledged anomaly
(existing)           POST /api/v1/admin/orders/{id}/ship              now 409s if an unacknowledged anomaly exists
```
Standard `ApiResponse<T>` envelope; lists ride `PagedResult<T>`; `[FromQuery]` PascalCase.

## 9. Frontend — Risk Queue + workbench badge

- **`features/admin/RiskQueuePage.tsx`** at `/admin/risk`: `DataTable` over `GET /analytics/anomalies` (order #, reason, score, detected-at) → **Acknowledge** button per row (`useAcknowledgeAnomaly` mutation, invalidates the list); `EmptyState` when clear. Mirrors `AuditLogPage`.
- **Gating:** new `ROLE_SETS.risk = ['Staff','StoreManager','Administrator']`; `SidebarNav` item "Risk queue" (area `risk`); `RoleGuard` on the route.
- **Workbench badge:** the admin order detail/list shows a "Flagged" badge when an order has an unacknowledged anomaly; the Mark-Shipped button surfaces the 409 message.
- `pnpm gen:api` + `lib/api/types.ts` aliases; **`pnpm format`** before push.

## 10. Authorization design

- **New policy** `Anomaly.Manage = RequireRole(Staff, StoreManager, Administrator)` (constant `Roles.Policies.AnomalyManage`), in the `staffPlus` group of `AddAuthorization`. Gates the risk-queue read **and** the acknowledge write.
- **Reuse:** the existing ship endpoint keeps its `Orders.Fulfill` policy; the anomaly block is a domain check inside it (not a policy).

## 11. Environment & secrets

- **Zero new config.** Anomaly detection is pure in-process math — no key, no Azure, no `Mode` flag (unlike Phase-4 sentiment / the chatbot). It runs identically in dev, CI, tests, and (eventually) prod.
- The hosted service runs everywhere; tests need no special config — its 15-min timer doesn't fire in a short test run, and the scan/ship logic is tested by direct invocation (§14).

## 12. Testing & E2E plan

- **`ZScoreScorerTests` (unit, `Retail.Ml`):** known sample → expected Z; σ==0 / σ<1e-6 → 0 (no divide-by-zero, not anomalous); determinism.
- **`OrderAnomalyServiceTests` (integration):** seed orders → each rule fires (≈5σ total; new country; qty 6) and a normal order does **not** flag; **a first-ever order is not new-country-flagged** (rule 2 needs ≥ 1 prior); idempotent (a second scan adds no duplicate row); guest + < 5-order customers use the **global** baseline for rule 1; the σ-guard returns not-anomalous on a near-empty store.
- **`AnomalyShipBlockTests` (integration):** Mark-Shipped on a flagged unacknowledged order → 409; **an un-scanned anomalous order is evaluated on ship and also 409s** (timing-bypass closed); after Acknowledge → ships (Paid→Fulfilled); a normal order ships without a flag (guards existing ship tests).
- **`RiskQueueTests` (integration):** `Anomaly.Manage` gate (Customer → 403, Staff/SM/Admin → 200); list returns unacknowledged newest-first; acknowledge flips state + removes from the queue.
- **Vitest:** `RiskQueuePage` (rows + Acknowledge + empty), the role-set assertion.
- **CI:** entirely keyless; the Coverlet 85% gate continues; the new `Retail.Ml` code is unit-covered.

## 13. Chunking (each independently buildable + verifiable)

- **C0 — Data model + policy.** `OrderAnomaly` entity + config + `Order.Anomalies` nav + `DbSet` + migration `0011_order_anomaly`; `Anomaly.Manage` policy. *Verify:* build 0/0, migration applies, indexes/FK present.
- **C1 — Synthetic order seeder.** `OrderDemoSeeder` (Development-only, deterministic; reuses the seeded catalog; satisfies the OrderNumber / member-XOR-guest CHECK / `ShippingAddressJson` invariants — §3.7); registered **after** the catalog seeder, invoked at startup. *Verify:* dev boot seeds ~6 months of Paid orders incl. the planted anomalies; idempotent on re-run (sentinel guard).
- **C2 — Anomaly engine.** `Retail.Ml/Anomaly/ZScoreScorer` (log-transform Z + σ-guard) + `OrderAnomalyService` (`ScanAsync` + `EvaluateOrderAsync`, in-memory per-customer rules, idempotent) + `OrderAnomalyHostedService` (15-min timer). *Verify (drive `ScanAsync` directly):* the planted anomalies flag; normal + first-ever orders don't; second scan is a no-op.
- **C3 — Risk queue + acknowledge + ship block + FE.** `GET /analytics/anomalies` + acknowledge endpoint; the `MarkShippedAsync` evaluate-on-ship 409 guard (via `IOrderRepository.HasUnacknowledgedAnomalyAsync`); `/admin/risk` page + nav + `ROLE_SETS.risk` + workbench badge. *Verify:* flagged order blocks ship → acknowledge → ships; an un-scanned anomalous order 409s on ship; **existing ship tests still pass** (confirm none ship a qty > 5 line); risk queue renders + clears; RBAC.
- **C4 — Tests, docs.** The full test set (§12); amend ADR-0003 (PLAN-§8d batch-scan as-built); reconcile DATABASE_DESIGN §5 (rows `0011_order_anomaly` / `0012_demand_forecast`) + add §19 as-built. *Verify:* all green in keyless CI; coverage gate holds.

## 14. Testing interactions (revised after scope review)

The original "the 15-min loop runs during tests" worry is mostly moot; three real
interactions to handle:

- **The hosted-service timer effectively never fires in tests.** `ApiFactory` boots
  hosted services but does **not** inject a `FakeTimeProvider`, so the `PeriodicTimer`
  runs on `TimeProvider.System` at a 15-min period and won't tick inside a short test
  run — which is exactly why the existing `CartExpirySweeper` /
  `ReviewSentimentHostedService` are registered **unconditionally** and cause no
  trouble. Gating the anomaly service off in Testing
  (`if (!builder.Environment.IsEnvironment("Testing")) AddHostedService<…>()`) is
  therefore **optional belt-and-suspenders** (it would be the codebase's first
  env-gated registration). Test the logic by resolving
  `IOrderAnomalyService.ScanAsync` / `EvaluateOrderAsync` directly (as
  `ChatToolExecutor` tests resolve the executor) + unit-testing the scorer.
- **The ship-block guard runs on every ship in tests** (evaluate-on-ship, §3.5).
  Existing fulfilment tests create **normal** orders (single normal-qty line, normal
  total, default address) → not flagged → ship proceeds. **C3 must confirm no
  existing ship test ships a `qty > 5` line** (rule 3) or an otherwise-anomalous
  order; if one does, acknowledge it first or normalize it.
- **Anomaly tests must scope to their own orders.** The Testcontainers SQL DB is
  shared across a class run (orders exist from other test classes), so anomaly
  assertions seed **dedicated** orders and assert only on those ids — never a blanket
  "no order is flagged".
- **The seeder self-gates on `IsDevelopment()`** (like `ReviewDemoSeeder`), so it does
  **not** run in the `Testing` startup path — tests build their own fixtures.

## 15. Resume-bullet alignment

This phase builds the **in-process foundation** for the **async / event-driven
(Job A-3)** bullet — and is honest that the bullet's headline numbers come later: the
`OrderAnomalyHostedService` (and the forecasting `BackgroundService` in part 2) are
the precursors that **Phase 8** migrates to Azure Functions (`OrderAnomalyScanFn`)
behind Service Bus / Event Grid, where the "10K+ events/day, 70% sync-call reduction"
numbers are actually measured. 5B-part-1 alone delivers the recurring-job pattern +
the analytics surface, not those metrics. The Z-score in `Retail.Ml`
is a defensible **ML/analytics** artifact (transparent, explainable, no model-training
or LLM claim — consistent with the saved "no LLM API claim" rule). Secondary:
REST/EF surface (risk-queue endpoints), the cross-cutting fulfilment guard (a
correctness/RBAC talking point), and testing/CI (keyless, deterministic).

## 16. Open items / follow-ups

- **Demand forecasting (Phase 5B part 2)** — ML.NET SSA, trainer CLI, `ml-train.yml`, `DemandForecast`/`ReorderHint`, migration `0012`; the DB-rows/$0 model decision (already taken) carries into it.
- **`CustomerSpendingBaseline` table + nightly rebuild** — the heavier ADR-0003 design, deferred (on-the-fly mean/σ suffices now).
- **Retro-scan window / `EvaluatedAt`** — if anomalies on older orders ever matter, add a watermark column (out now, §3.4).
- **`OrderAnomalyScanFn` (Phase 8)** — the in-process service moves to a Function; the A-3 numbers are measured there.
- **Order-list "has-anomaly" filter (REQUIREMENTS §4.2)** — the admin order workbench's filter-by-flagged option. The *badge* ships this phase (§9); the list **filter** is deferred to a Phase-3-workbench follow-up.
- **Anomaly summary report (REQUIREMENTS §11.2, "异常汇总")** — a summary/trend report tile; deferred (the operational risk queue covers the immediate need).

## 17. Known limitations (5B anomaly)

- **In-process, single-instance:** like the other hosted services, it runs on every instance with no leader election; at portfolio scale that's fine (flagging is idempotent on the existing-row check). The durable multi-instance version is Phase 8.
- **Recent-window scan:** orders older than 14 days are never retro-flagged (§3.4) — acceptable, anomalies matter pre-fulfilment.
- **Seeder audit noise:** the dev seeder inserts real `Order`/`Payment` rows, which the `AuditTrailInterceptor` records — accepted dev-only noise (the seeder is never run outside Development).
- **Guest baseline:** guest orders (no profile/history) fall to the **global** rule-1 baseline and skip rule 2 (no prior orders) — they have no per-customer history (§4 row 4).
- **Acknowledge-vs-ship TOCTOU:** the ship guard's anomaly check + the Paid→Fulfilled SaveChanges aren't one transaction with a concurrent acknowledge, so the two could interleave. The order's `RowVersion` guards the status transition but not the anomaly row; at portfolio scale (a single operator) this is benign — documented, not closed.

## 18. As-built reconciliation (shipped C0–C4)

Built across five commits on `main` (C0 data model → C1 seeder → C2 engine → C3 risk queue/ship-block
→ C4 docs), each reviewed + CI-green. Deltas from the plan above:

- **Migration `0011_order_anomaly`** (anomaly table only); forecasting → `0012_demand_forecast` later.
  DATABASE_DESIGN §5 updated to the two-row split. ADR-0003 **amended** (the PLAN-§8d batch-scan as-built
  supersedes its `CustomerSpendingBaseline` fraud design; the shared scorer landed at
  `Retail.Ml/Anomaly/ZScoreScorer.cs`, not `Retail.Ml/Fraud/`).
- **Engine (C2):** `ZScoreScorer` (pure, population-σ, log-Z, σ-guard) + `OrderAnomalyService`
  (`ScanAsync` batch + `EvaluateOrderAsync` single) computing the per-customer rules **in memory**
  (country lives in `ShippingAddressJson`). One row per flagged order; `Score` = `|Z|` or 0.
- **Hosted service scans IMMEDIATELY on startup** (do-while), so the Risk Queue populates on a
  boot/deploy — which made the **Testing-environment registration gate required** (not the "optional"
  of §14): an immediate scan would otherwise flag other integration tests' orders.
- **Seeder tuning (caught on the first dev run):** the injected Z-anomaly wasn't `> 3σ` against the
  *wide* original baseline once log-transformed (engine correct, seed under-sized). Fixed in C1's
  seeder — normals tightened to 1–2 lines × qty 1–2, the anomaly enlarged to every variant × qty 5 —
  and locked by an `EvaluateOrder_BigTotalAgainstWideBaseline` test.
- **Acknowledge** returns `200` + an `ApiResponse` envelope (not `204`), matching the codebase's
  envelope convention.
- **Workbench "Flagged" badge DEFERRED** (needs `AdminOrderSummary/Detail` DTO + a list join) — folds
  with the §16 order-list has-anomaly filter; the ship-block 409 + the Risk Queue already surface
  anomalies, so the feature is complete without it.
- **Tests (all hermetic, keyless):** `ZScoreScorerTests` (5 unit) · `OrderAnomalyServiceTests` (8
  integration, driven via `EvaluateOrderAsync` scoped to each test's own order) · `RiskQueueTests` (9
  integration: RBAC/list/acknowledge/ship-block) · `RiskQueuePage` (2 Vitest). The cross-phase
  ship-block edit broke no existing fulfilment test.
