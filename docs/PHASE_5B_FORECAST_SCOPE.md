# Phase 5B (part 2) — Demand Forecasting (Holt-Winters): Implementation Scope

> Authoritative pre-build scope for **Phase 5B, part 2 — demand forecasting + reorder hints**
> (Epic 5, `PLAN.md` §8c; REQUIREMENTS §9; DATABASE_DESIGN §3.17/§3.18). The **order-anomaly**
> half of Phase 5B already shipped (`PHASE_5B_SCOPE.md`). Where this doc disagrees with
> `PLAN.md` / `REQUIREMENTS.md` / `DATABASE_DESIGN.md`, **this doc wins for the sub-phase** —
> deltas are in §3 (decisions) and §4 (drift). Companion to `PHASE_5B_SCOPE.md` and the earlier
> phase scopes. Source of truth for the sub-phase.

## 1. Goal & demo target

The order-anomaly half gave Phase 5B its event-driven artifact; this half gives it the project's
**time-series** artifact: **per-variant demand forecasting with Holt-Winters** (additive triple
exponential smoothing — level + trend + weekly seasonality) + **reorder hints**. Like anomaly, it's
**$0 / keyless** and **dependency-free** — the model is pure C# (no native ML runtime), computes
**in-process**, and the results are written as rows; no Azure, no API key. (We planned ML.NET SSA but
its Intel-MKL/`libiomp5` native dependency is absent on stock Linux — see §4 row 6.)

**The flow (REQUIREMENTS §9):** a daily `ForecastRefreshHostedService` builds each active variant's
180-day daily-demand series (zero-filled), fits a Holt-Winters model, and writes a **`DemandForecast`**
row (14-day-ahead quantity + an 80% prediction band from the in-sample residual σ) + a **`ReorderHint`**
(`max(0, forecast₁₄d + safetyStock − onHand)`). The back-office sees a **`/admin/forecast`** page —
a per-variant Recharts line with the band + a reorder-hints list with **Dismiss**.

**Demo (acceptance bar, `PLAN.md` §8c / REQUIREMENTS §9):** on a fresh dev run the 6-month synthetic
order seeder (already shipped, 5B-anomaly C1) gives the forecaster real history; the refresh writes
forecasts + reorder hints; `/admin/forecast` shows a variant's forecast with its 80% band and a
ranked reorder list; dismissing a hint removes it. Variants with `< 30` days of history show "Forecast
warming up". Flipping `Forecast:Mode=stub` yields instant flat forecasts with no fit (for a
fresh/empty clone, CI, and tests).

## 2. Scope boundary

**In:**
- **`DemandForecast` + `ReorderHint`** entities (DATABASE_DESIGN §3.17/§3.18) + migration **`0012_demand_forecast`**; FK → `ProductVariant`.
- **`Retail.Ml/Forecasting/`** (pure C#, **no ML.NET / no native deps**): a **`DailySeriesBuilder`** (zero-fill a sparse daily series), an **`IDemandForecaster`** with **`HoltWintersForecaster`** (additive triple-exp smoothing + a residual-σ band) + **`StubDemandForecaster`** (flat trailing-mean), a pure **`ForecastMath`** (clamp ≥ 0 + quadrature total-band), and the result records.
- **`IForecastService` / `ForecastService`** (scoped) — `RefreshAsync`: per active variant, query the 180-day series → forecaster → write a `DemandForecast` row + upsert a `ReorderHint`; cold-start skip; `DismissReorderHintAsync`.
- **`ForecastRefreshHostedService` : BackgroundService** — daily `PeriodicTimer` (injected `TimeProvider`), immediate-on-startup, scoped per tick, log-and-continue — mirror of `OrderAnomalyHostedService`; **gated OFF in Testing**.
- **`Forecast:Mode`** seam (`hw` | `stub`, default `hw`; the config-selected-impl mechanism of `Ai:Mode`) selecting the forecaster at DI.
- **API**: `GET /api/v1/analytics/forecast` (latest per variant + CI), `GET /api/v1/analytics/reorder-hints` (active, ranked), `POST /api/v1/analytics/reorder-hints/{id}/dismiss` — all gated by a **new `Forecast.View` policy** (Staff + StoreManager + Administrator).
- **FE**: a dedicated **`/admin/forecast`** page — per-variant **Recharts** line + 80% confidence band + a reorder-hints list with **Dismiss**; `SidebarNav` item + `ROLE_SETS.forecast` + route.
- **`Retail.Ml.Trainer` CLI** (scaffold) — a runnable console that reuses the **same** `ForecastService` (C2) + `RetailDbContextFactory` to recompute + write rows on demand (`dotnet run`).
- **`ml-train.yml`** — a **build-only / `workflow_dispatch`** GitHub Action proving the nightly-pipeline shape (no Azure, no cron execution).
- **Hermetic tests** (no Azure, no key): `DailySeriesBuilder` + `ForecastMath` + forecaster unit tests (Holt-Winters is deterministic → exact assertions), `ForecastService` integration (Holt-Winters on seeded data + cold-start + reorder math + dismiss + RBAC), `/admin/forecast` Vitest.
- **An ADR** recording the DB-rows/$0 decision **and the SSA→Holt-Winters pivot** (the MKL native-dep rationale).

**Out / deferred (→ Phase 8 unless noted):**
- **Azure Blob `ml-models/` + `ModelStore` + a persisted/serialized model** (REQUIREMENTS §9.1/§9.2) → moot here (Holt-Winters refits in-process from rows — nothing to serialize); revisit in Phase 8 only if a heavier persisted model is introduced.
- **`ml-train.yml` as a REAL nightly cron** against a live DB → Phase 8 (it's a build-only scaffold here).
- **`ForecastRefreshFn` Azure Function** → Phase 8 (5B ships the in-process `BackgroundService`).
- **ML.NET SSA** (and any native-runtime model) → dropped for the MKL/`libiomp5` Linux dep (§4 row 6); a managed SSA or ML.NET-on-Linux revisit is a Phase-8+ option.
- **Multivariate / promotional-uplift / price-elasticity forecasting** → never in scope (univariate Holt-Winters on quantity).

## 3. Key decisions (2026-06-21)

### 3.1 In-process is the real path; the Trainer CLI + `ml-train.yml` are a scaffold (user-confirmed)

**Decision:** the **`ForecastRefreshHostedService`** (in-process, daily) is the real forecasting runner
for 5B — it computes Holt-Winters forecasts from the seeded orders and writes `DemandForecast`/`ReorderHint`
rows, exactly mirroring `OrderAnomalyHostedService`. The **`Retail.Ml.Trainer` CLI** is *also* built as
a runnable console (a manual recompute), and **`ml-train.yml`** is a **build-only `workflow_dispatch`
scaffold** that proves the nightly-pipeline shape. **Why:** keeps dev/CI/demo entirely $0/in-process
(the locked DB-rows decision) while still shipping the tangible "forecasting pipeline" artifact; the
real Azure cron + Blob is Phase 8. The CLI **reuses `IForecastService.RefreshAsync`** (it takes a
`ProjectReference` to `Retail.Api` for the service + `RetailDbContextFactory` — a deliberate tool→app
reference; §13 C4), so the service and the CLI write rows through the **same** path — no logic
duplication.

### 3.2 DB-rows / $0 (carried from 5B planning)

**Decision:** forecasts are **computed then written as rows** (`DemandForecast`, `ReorderHint`); the API
just reads rows. **No** Azure Blob, **no** `ModelStore`, **no** lazy `.zip` inference. **Why:** $0,
keyless, hermetic, simplest read path; matches PLAN's in-process-now / Function-later staging. This
**supersedes REQUIREMENTS §9.1/§9.2** (Blob + ModelStore) — recorded in a new ADR (§13 C4).

### 3.3 `Forecast:Mode` = `hw` (default) | `stub`

**Decision:** an `IDemandForecaster` selected at DI by `Forecast:Mode` — the same config-selected-impl
**mechanism** as `Ai:Mode`, but the **default is `hw`** (the real model), not stub. (`Ai:Mode` defaults
to `stub` because it needs a paid key; forecasting needs none, so real-by-default is correct.) **`hw`**
= `HoltWintersForecaster` (pure C#, deterministic — no seed/native runtime needed); **`stub`** = a flat
trailing-mean forecast (±20% band, `Confidence = 0.5`) for a fresh/empty clone, CI, and tests. **Why:**
the real model needs history but cold-start (§3.6) handles the no-data case gracefully, so `hw` is safe
as the default; the stub gives instant output with zero fit. No key, no `ValidateOnStart` secret
(unlike `Ai`) — pure compute.

### 3.4 `ForecastedQty` is the 14-day total; the band is by quadrature; outputs are clamped ≥ 0

Holt-Winters produces **per-day** point forecasts; the per-day 80% band is `point ± z·σ·√h`
(`z ≈ 1.2816`, `σ` = the in-sample one-step residual stddev, widening with the step `h`). The doc
collapses the horizon to one stored row, and **how** matters (these two were HIGH findings on the
original SSA plan and carry over unchanged):

- **Clamp first (review F2):** demand can't be negative, and a sparse/young series can push the band
  (or a declining forecast) below 0. So each per-day `Forecast`, `Lower`, `Upper` is `max(0, ·)`
  **before** any aggregation; the chart band (§9) therefore floors at 0.
- **`ForecastedQty`** = Σ of the (clamped) 14 per-day forecasts — the lead-window total the reorder
  formula consumes.
- **Band by quadrature, NOT sum-of-bounds (review F1):** the band of a 14-day *total* is **not** the
  sum of per-day bands. Store `halfWidth = sqrt(Σ (Upperᵢ − Forecastᵢ)²)`, `LowerBound = max(0,
  ForecastedQty − halfWidth)`, `UpperBound = ForecastedQty + halfWidth` — the standard independent-errors
  propagation. (`ForecastMath.Summarize` is the pure, fully-tested home of this clamp + quadrature.)
- **`Confidence`** (0..1) = `clamp(daysOfHistory / TrainSize(180), 0, 1)` for a real forecast, `0.5`
  for stub. **Labelled honestly as a data-sufficiency proxy** (how much history backs the forecast),
  not a calibrated statistical confidence.

### 3.5 Reorder hint = one upserted row per variant; Dismissed sticks

**Decision:** `RecommendedOrderQty = max(0, forecast₁₄d + safetyStock − onHand)`, `safetyStock =
1.65 · σ · √leadTimeDays`, `σ` = stddev of the 180-day zero-filled daily series, `leadTimeDays = 7`,
`z = 1.65` (both `Forecast`-configurable). Each daily refresh **upserts one `ReorderHint` per variant**
(find-by-`ProductVariantId` → update qty/reasoning/`GeneratedAt`, else insert); the list shows
`!Dismissed && RecommendedOrderQty > 0`. **A dismissed hint stays dismissed** (the refresh updates its
qty but leaves `Dismissed = true`) so Dismiss is meaningful. **Why:** `DemandForecast` keeps history
(append + read-latest), but a reorder hint is an actionable to-do — one live row per variant, and
dismissing it must persist.

### 3.6 Cold-start + degenerate series → skip the variant (no row)

**Decision:** a variant is **skipped** (no `DemandForecast`/`ReorderHint` row; FE shows "Forecast
warming up") when **either** its order history spans `< 30` days (first→last order, a cheap
`MIN/MAX(PlacedAt)` per variant) **or** it has **fewer than a floor of non-zero demand-days** (e.g.
`< 14`) in the 180-day window. **Why:** the model on a near-empty / mostly-zero series produces a
meaningless (flat-near-zero) forecast — abstaining beats surfacing noise. (Holt-Winters also has an
internal flat-mean fallback for series shorter than two seasons; the service-level skip is the primary guard.)
The non-zero-days floor (not just calendar age) is the real guard: the per-variant demand is sparse
even at 180 calendar days (there's no catalog seeder; the synthetic orders spread thin across variants).
A sentinel `Confidence = 0` row (REQUIREMENTS §9.3 wording) is **not** written — absence = warming up
(§4 drift). The skip threshold (`MinHistoryDays`, `MinNonZeroDays`) is `Forecast`-configurable.

### 3.7 RBAC = new `Forecast.View` policy (Staff + StoreManager + Administrator)

**Decision:** `Forecast.View = RequireRole(staffPlus)` gates the forecast read, the reorder-hints read,
**and** the dismiss write. **Why:** the REQUIREMENTS matrix puts *查看预测和补货建议* + *关闭补货提示* at
**Staff ✅** (like anomaly, unlike sentiment/chat); a dedicated named policy keeps the capability matrix
legible (mirrors `Anomaly.Manage`).

## 4. Doc-vs-spec drifts this sub-phase fixes (recon-verified)

| # | Doc / spec says | Reality → action |
|---|---|---|
| 1 | DATABASE_DESIGN §3.17/§3.18: PK `newsequentialid()`, `GeneratedAt datetime2(7) sysutcdatetime()` | As-built convention: GUID PKs are **client/EF-generated**; timestamps are **`datetimeoffset`** service-stamped via `TimeProvider`. `IAuditableEntity` on both. |
| 2 | REQUIREMENTS §9.1/§9.2: train `.zip` → Azure Blob `ml-models/`; API `ModelStore` lazy-load + `IMemoryCache` | **Superseded** by DB-rows/$0 (§3.2): in-process compute → rows; no Blob/ModelStore. Recorded in a new ADR. Blob/Function → Phase 8. |
| 3 | REQUIREMENTS §9.1: nightly via `ml-train.yml` GitHub Actions cron | The real daily runner is the **in-process** `ForecastRefreshHostedService`; `ml-train.yml` is a **build-only scaffold** (§3.1). |
| 4 | REQUIREMENTS §9.3: `< 30` days → "`Confidence=0`" (implies a row) | As-built: **skip the variant** (no row); UI infers "warming up" from absence (§3.6). |
| 6 | REQUIREMENTS §9.1 / PLAN §8c: **ML.NET SSA** (`ForecastBySsa`) | As-built: **pure-C# Holt-Winters** (additive triple-exp smoothing). **Why:** ML.NET SSA's eigen-decomposition needs Intel **MKL** (`libiomp5.so`), which is absent on stock Linux dev + CI and not in the NuGet redist (verified: `ldd libMklImports.so` → `libiomp5.so => not found`). The planning probe checked compile, not the runtime fit. Holt-Winters is dependency-free, deterministic, $0/hermetic, and whiteboard-defensible. A managed-SSA or MKL-provisioned ML.NET revisit is a Phase-8+ option. Recorded in the new ADR (§13 C4). |
| 5 | DATABASE_DESIGN §3.18: `IX_ReorderHint_ProductVariantId_Dismissed_RecommendedOrderQty` (non-unique, implies many rows) | As-built: **one upserted row per variant** (§3.5); the composite index still backs the ranked "top reorder" list. |
| 7 | DATABASE_DESIGN §3.17 labels `LowerBound`/`UpperBound` as the "80% CI" (with no formula) | As-built: per-day forecaster outputs are clamped ≥ 0, then the **total** band is **quadrature-propagated** (`√Σ(Upperᵢ−Forecastᵢ)²`), not a naive sum of per-day bounds (§3.4) — a documented independent-errors approximation, lower-floored at 0. |

## 5. Data model — migration `0012_demand_forecast`

Both entities are `IAuditableEntity`, client-gen GUID PK, `datetimeoffset` timestamps, FK → `ProductVariant` **Cascade** (single FK; forecasts/hints are disposable derived rows that should die with the variant), **not** on the `AuditTrailInterceptor` allowlist (high-volume, system-generated). One-directional FK (no `ProductVariant.Forecasts` back-collection); reads are direct queries.

- **`DemandForecast`**: `Id`; `ProductVariantId` FK; `Horizon` smallint (14); `ForecastedQty` `decimal(10,2)` (14-day total, clamped ≥0); `LowerBound`/`UpperBound` `decimal(10,2)` (80% band — quadrature-propagated, lower-floored at 0; §3.4); `Confidence` `decimal(4,3)` (data-sufficiency proxy); `ModelVersion` `nvarchar(40)` (ISO date of the run, or `"stub"`); `GeneratedAt` `datetimeoffset`; audit cols. **Index** `IX_DemandForecast_ProductVariantId_GeneratedAt` (latest per variant). **Append per refresh** (history retained — see §17 on bounded growth/pruning).
- **`ReorderHint`**: `Id`; `ProductVariantId` FK; `RecommendedOrderQty` int; `Reasoning` `nvarchar(400)`; `GeneratedAt` `datetimeoffset`; `Dismissed` bit (default 0); audit cols. **Index** `IX_ReorderHint_ProductVariantId_Dismissed_RecommendedOrderQty`. **One upserted row per variant** (§3.5).
- No new enum (the `Dismissed` bit suffices). `RetailDbContext` gains `DemandForecasts` + `ReorderHints` `DbSet`s; configs auto-discovered.

## 6. Forecaster — `Retail.Ml/Forecasting/`

- **`DailySeriesBuilder`** (pure): given per-day demand + a window end + `length`, returns a zero-filled `float[]` (most-recent-last). Unit-testable in isolation.
- **`IDemandForecaster`** → `Forecast(IReadOnlyList<float> series, int horizon)` returns `HorizonForecast { IReadOnlyList<float> Forecast, LowerBound, UpperBound }` (each length = `horizon`; `Confidence` is computed by the service from history, not the forecaster).
  - **`HoltWintersForecaster`** (pure C#): additive triple-exp smoothing (level + trend + weekly seasonal, defaults α 0.3 / β 0.1 / γ 0.3, season 7), per-step band `± z·σ·√h` from the in-sample residual σ; a flat-mean fallback when the series is shorter than two seasons. Deterministic, no native deps → **fully covered** (no `[ExcludeFromCodeCoverage]` needed).
  - **`StubDemandForecaster`**: deterministic flat trailing-mean ±20% (§3.3).
  - **`ForecastMath.Summarize`** (pure): clamps the per-day output ≥ 0, sums → `ForecastedQty`, and quadrature-propagates the total band (§3.4) — the covered home of that logic.
- **`ForecastService`** (scoped, `Retail.Api`): for each active variant — (1) load the raw `OrderLine`+`Order` rows (a translatable predicate: `PaidStatuses.Contains(o.Status)` + `o.PlacedAt ≥ cutoff`, `AsNoTracking`) and **group-by-day + zero-fill IN MEMORY** (EF can't translate a `day(PlacedAt)` grouping — exactly the trap the anomaly/`ReportQueryService.GetSalesByDayAsync` pattern handles in memory); (2) cold-start/degenerate skip (§3.6, from a cheap per-variant `MIN/MAX(PlacedAt)` + non-zero-day count); (3) `DailySeriesBuilder` → `IDemandForecaster` → write a `DemandForecast` (§3.4: clamp, sum, quadrature band); (4) read the variant's `InventoryItem.OnHand`, compute + **upsert** a `ReorderHint` (§3.5). Statuses **{Paid, Fulfilled}** (note: demo orders are Paid-only — the Fulfilled branch is correct but unexercised by the seeder; §17).

## 7. Refresh job + reorder

- **`ForecastRefreshHostedService`** — `PeriodicTimer(24h, _timeProvider)`, do/while (immediate-on-startup so the dashboard fills on boot), `CreateAsyncScope()` per tick → `IForecastService.RefreshAsync`, per-tick try/catch, `OperationCanceledException` on shutdown. **Registered OFF in Testing** (immediate scan would perturb integration tests — §14).
- **Reorder math** (§3.5) computed in `ForecastService` from the same 180-day series (`σ`) + the variant's current `InventoryItem.OnHand`.

## 8. API surface (new, on `AnalyticsController`)

```
GET  /api/v1/analytics/forecast?Page&PageSize        (Forecast.View)  latest DemandForecast per variant (+ band, confidence)
GET  /api/v1/analytics/reorder-hints?Page&PageSize    (Forecast.View)  active hints, ranked by RecommendedOrderQty desc
POST /api/v1/analytics/reorder-hints/{id}/dismiss     (Forecast.View)  → 200; 404 if missing
```
- `ApiResponse<T>` envelope; both lists ride `PagedResult<T>` with `Page`/`PageSize` query params; reads on `IReportQueryService`, the dismiss on `IForecastService` (the mutation lives on the domain service, mirroring `AcknowledgeAsync` on `IOrderAnomalyService`).
- **"Latest per variant" must be a correlated subquery**, not a naive `GroupBy` (EF can't translate group-by-then-take-latest): `where f.GeneratedAt == DemandForecasts.Where(x => x.ProductVariantId == f.ProductVariantId).Max(x => x.GeneratedAt)`.
- The forecast + reorder DTOs **carry the variant label** (`ProductVariantId`, `Sku`, product `Name`) — the FE variant selector + the reorder list need it (the entity has only the FK).

## 9. Frontend — `/admin/forecast`

- **`features/admin/ForecastPage.tsx`**: a per-variant **Recharts** line (forecast + a shaded 80% `LowerBound`/`UpperBound` band) with a variant selector; a **reorder-hints** `DataTable` (variant, recommended qty, reasoning) with a **Dismiss** action; loading/error/empty + "Forecast warming up" states.
- **Gating:** new `ROLE_SETS.forecast = ['Staff','StoreManager','Administrator']`; `SidebarNav` item "Forecast"; `RoleGuard` route.
- `useForecast` + `useReorderHints` + `useDismissReorderHint` hooks (mirror `useRiskQueue`); `pnpm gen:api` + `lib/api/types.ts` aliases; **`pnpm format`** before push.

## 10. Authorization design

- **New policy** `Forecast.View = RequireRole(Staff, StoreManager, Administrator)` (constant `Roles.Policies.ForecastView`), in the `staffPlus` group. Gates the two reads **and** the dismiss write (low-stakes UX action; mirrors `Anomaly.Manage` covering read + acknowledge).

## 11. Environment & secrets

- **Zero new secrets.** `Forecast:Mode` (default `hw`), `Forecast:LeadTimeDays` (7), `Forecast:ServiceLevelZ` (1.65, the reorder safety-stock z), and the Holt-Winters smoothing/season params are plain config. The forecaster is **pure C#** — no key, no Azure, no native runtime. Runs identically in dev/CI/tests (gated hosted service) / future-prod.

## 12. Testing & E2E plan

- **`DailySeriesBuilderTests` (unit):** sparse points → correct zero-filled length/order; window boundaries.
- **`DemandForecasterTests` (unit):** `StubDemandForecaster` determinism; `HoltWintersForecaster` — deterministic, so assert exactly: a flat series recovers the level (≈ zero band), a trending series projects upward, a sparse series stays finite/non-negative, and a too-short series hits the flat-mean fallback. `ForecastMathTests` exact-tests the clamp + quadrature band (incl. that it is NOT the naive sum of per-day bounds).
- **`ForecastServiceTests` (integration):** seed a variant with > 30 days of orders → `RefreshAsync` writes a `DemandForecast` + a `ReorderHint`; a `< 30`-day / too-sparse variant is **skipped** (no row); reorder math (`max(0, f₁₄d + safety − onHand)`); upsert keeps one hint per variant; dismiss hides it. RBAC on the endpoints (Customer → 403, Staff/SM/Admin → 200).
- **Vitest:** `ForecastPage` (chart renders from mocked data; reorder list + Dismiss; warming-up empty state); the `roleSets.forecast` capability assertion.
- **CI:** keyless; Coverlet 85% gate — the Holt-Winters forecaster + the sum/band/clamp/series math are **all pure and covered** (no `[ExcludeFromCodeCoverage]` needed now that SSA's native glue is gone). `ml-train.yml` builds the Trainer (proves it compiles), runs nothing.

## 13. Chunking (each independently buildable + verifiable)

- **C0 — Data model + policy.** `DemandForecast` + `ReorderHint` entities + configs + `DbSet`s + migration `0012_demand_forecast`; `Forecast.View` policy. *Verify:* build 0/0, migration applies, indexes/FK present.
- **C1 — Forecaster.** `Retail.Ml/Forecasting/` (`DailySeriesBuilder`, `ForecastMath`, `IDemandForecaster` + `HoltWintersForecaster` + `StubDemandForecaster`, `HorizonForecast`/`DemandForecastSummary`) — pure C#, no packages — + unit tests + the `Forecast:Mode` seam. *Verify:* Holt-Winters recovers a flat level + projects a trend (deterministic); stub deterministic; build clean. **(Built — the SSA→Holt-Winters pivot happened here; §4 row 6.)**
- **C2 — Refresh service + hosted service.** `IForecastService`/`ForecastService` (series query → forecaster → write `DemandForecast` + upsert `ReorderHint`; cold-start skip; dismiss) + `ForecastRefreshHostedService` (daily, gated) + Program.cs registration (`Forecast:Mode`). *Verify (drive `RefreshAsync` directly):* seeded variant → forecast + hint; `< 30`d skipped; second run upserts (no dup hint).
- **C3 — API + FE.** The three endpoints; `/admin/forecast` page (Recharts CI-band + reorder list + Dismiss) + nav + `ROLE_SETS.forecast` + route. *Verify:* forecast + hints render; dismiss hides; RBAC; gen:api/types/format.
- **C4 — Trainer CLI + ml-train.yml + docs.** `Retail.Ml.Trainer` becomes a runnable CLI that **reuses `IForecastService.RefreshAsync` (C2)** — it gains a **`ProjectReference` to `Retail.Api`** (to reach `RetailDbContextFactory` + the `RetailDbContext`/entities/`ForecastService`; a deliberate tool→app reference, the design-time-factory's purpose) + its own `Microsoft.Extensions.Configuration/Logging` host wiring. So the CLI and the hosted service write rows through the **same** path (no duplication). `ml-train.yml`: a `workflow_dispatch` job that **builds + runs the Trainer's unit-touch / `dotnet build`** of `Retail.Ml.Trainer` (proves it compiles; no Azure, no cron), SHA-pinned actions per `ci.yml`. Plus the new **ADR** (DB-rows/$0 forecast decision — a new ADR, not an ADR-0003 amendment, since 0003 is the anomaly Z-score); DATABASE_DESIGN §5 (`0012`), REQUIREMENTS §9 reconciliation, and a **new scope §18 as-built** section. *Verify:* `dotnet run --project Retail.Ml.Trainer` writes rows against the dev DB; CI green incl. the build-only workflow.

## 14. Known testing concern — the always-on hosted service

Same as anomaly: `ForecastRefreshHostedService` scans immediately on startup, so its **registration is
gated OFF in the `Testing` environment** (an immediate refresh would write rows mid-test). Tests drive
`IForecastService.RefreshAsync` directly + unit-test the forecaster/series builder. The seeder
self-gates on `IsDevelopment()`.

## 15. Resume-bullet alignment

The **time-series** artifact: "per-variant demand forecasting with Holt-Winters (triple exponential
smoothing) + an 80% prediction **band**, feeding automated reorder recommendations" — defensible
end-to-end (level/trend/seasonal smoothing, zero-fill, the residual-σ band, the quadrature
total-band-propagation + its independence caveat, and the safety-stock formula all explainable on a
whiteboard; no LLM/ML-over-claim, consistent with the "no LLM API claim" rule). The band is a propagated
14-day-total interval (§3.4), described honestly — not a per-day calibrated CI. The `Retail.Ml.Trainer`
CLI + build-only `ml-train.yml` give the **forecasting-pipeline** talking point (GitHub Actions,
ready for Phase-8). Secondary: the `ForecastRefreshHostedService` is a second `BackgroundService →
Phase-8 Function` precursor (the async/event-driven A-3 story anomaly already anchors); plus the React
data-viz (prediction-band chart). (Honest note: ML.NET SSA was the plan but its MKL native dep is
absent on Linux — §4 row 6; "implemented + de-scoped ML.NET SSA after a runtime native-dependency
blocker, pivoted to a managed model" is itself a credible engineering-judgment talking point.)

## 16. Open items / follow-ups

- **Blob `ModelStore` + `.zip` inference + real `ml-train.yml` cron + `ForecastRefreshFn`** → Phase 8 (the locked-deferred half).
- **Forecast accuracy/backtesting** (MAPE/RMSE vs held-out) — not measured this sub-phase; a follow-on.
- **Per-variant lead times** (currently one global `LeadTimeDays`) — config-flat now.

## 17. Known limitations

- **Univariate Holt-Winters on quantity** — no promo/price regressors; captures level + trend + weekly seasonality only.
- **In-process, single-instance** — like the other hosted services; the durable multi-instance/Function version is Phase 8.
- **Cold-start variants are invisible** until 30 days of history + enough non-zero demand-days accrue (§3.6) — by design.
- **`ml-train.yml` doesn't execute** — build-only scaffold; the real nightly is in-process (or Phase-8 Azure).
- **Safety stock under intermittent demand** — `1.65·σ·√7` over the zero-inflated daily series uses a normal approximation that mis-fits sparse/lumpy demand (σ deflated by the zeros). It's the spec formula (REQUIREMENTS §9.2) and fine for the demo; a Croston/intermittent-demand method is the honest upgrade — noted, not built.
- **`DemandForecast` grows append-only** — one row per variant per daily refresh. Bounded at portfolio scale; a retention prune (keep last N per variant) is a trivial follow-on if it ever matters.
- **The `Fulfilled` series branch is unexercised on demo data** — the status filter is `{Paid, Fulfilled}` (correct), but the seeder writes Paid-only orders, so only the Paid path runs in dev/CI; a fulfilment-flow test would exercise the rest.

## 18. As-built reconciliation (shipped C0–C4)

Built across five commits on `main` (C0 data model → C1 forecaster → C2 refresh service → C3 API + FE
→ C4 trainer + docs), each reviewed + CI-green.

- **The headline pivot — ML.NET SSA → pure-C# Holt-Winters (ADR-0012, §4 row 6).** The plan was
  ML.NET SSA, but `ForecastBySsa` needs Intel MKL (`libiomp5.so`), absent on Linux dev + CI and not in
  the NuGet redist (`ldd libMklImports.so` → `libiomp5.so => not found`; the planning probe checked
  compile, not the runtime fit). Pivoted to a dependency-free Holt-Winters model — more hermetic than
  planned, fully covered (no `[ExcludeFromCodeCoverage]`), and deterministic. The `IDemandForecaster`
  seam + `ForecastMath` + `DailySeriesBuilder` + stub were unaffected.
- **The two pre-code review HIGHs hold (F1/F2):** per-day outputs are clamped ≥ 0; the 14-day-total band
  is quadrature-propagated (`√Σ(Upperᵢ−Forecastᵢ)²`), lower-floored at 0 — not a naive sum of bounds.
- **Migration `0012_demand_forecast`** (DemandForecast append-per-refresh + ReorderHint upsert-one-per-
  variant, Dismissed sticks); FK → ProductVariant **Cascade**; new `Forecast.View` policy (Staff+).
- **In-process is the real runner:** `ForecastService.RefreshAsync` (grouped + zero-filled in memory) +
  the daily `ForecastRefreshHostedService` (immediate-on-startup, gated OFF in Testing). The
  `Retail.Ml.Trainer` CLI reuses `RefreshAsync` (verified: writes rows against the dev DB); `ml-train.yml`
  is a build-only `workflow_dispatch` scaffold.
- **FE chart deviation from §9:** the stored forecast is a 14-day **total** (not a daily series), so
  `/admin/forecast` shows a per-variant **bar + upper/lower band lines** across variants (Recharts
  `ComposedChart`), not a per-variant time-series **line + variant selector**. Same band data, a shape
  that fits the as-built rows. The reorder-hints table + Dismiss are as specced.
- **`Confidence`** is a labelled data-sufficiency proxy (`clamp(daysOfHistory/180)`), not a calibrated CI.
- **Tests (all hermetic, keyless):** `DailySeriesBuilderTests` (3) · `ForecastMathTests` (3) ·
  `DemandForecasterTests` (5, Holt-Winters deterministic) · `ForecastServiceTests` (5, drive
  `RefreshAsync`) · `ForecastApiTests` (6, RBAC/list/dismiss) · `ForecastPage` Vitest (2) +
  the `roleSets.forecast` assertion. Dev-boot proof: "Forecast refresh wrote 7 of 8 active variants".
