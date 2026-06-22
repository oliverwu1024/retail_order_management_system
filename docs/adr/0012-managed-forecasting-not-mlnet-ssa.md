# ADR-0012: Managed Holt-Winters Forecasting (DB-rows, $0) — Not ML.NET SSA on Blob

**Status**: Accepted (2026-06-22)

**Deciders**: project owner

**Related**: PLAN.md §8c (demand forecasting) · REQUIREMENTS.md §9 · DATABASE_DESIGN.md §3.17/§3.18 · ADR-0003 (Z-score, the anomaly sibling) · PHASE_5B_FORECAST_SCOPE.md

---

## Context

Phase 5B's second half is **per-variant demand forecasting + reorder hints**. The plan
(REQUIREMENTS §9.1/§9.2, PLAN §8c) was **ML.NET SSA** (`ForecastBySsa`): train a per-variant model,
serialize `model-{variantId}.zip` to an **Azure Blob** `ml-models/` container nightly via a GitHub
Action, and have the API lazy-load models through a `ModelStore` + `IMemoryCache`.

Two facts reshaped this during implementation:

1. **The locked DB-rows / $0 decision (5B planning).** To keep the whole feature keyless and hermetic
   (matching the order-anomaly half), forecasts are computed in-process and written as
   `DemandForecast` / `ReorderHint` **rows**; the API just reads rows. That already removed the Blob +
   `ModelStore` + lazy-`.zip` path (deferred to Phase 8).

2. **ML.NET SSA does not run on the project's Linux toolchain.** `ForecastBySsa`'s eigen-decomposition
   calls into Intel **MKL**; `libMklImports.so` has a hard `NEEDED` dependency on **`libiomp5.so`**
   (Intel OpenMP), which is **absent on the dev box and on stock GitHub-Actions Ubuntu**, and is **not
   shipped** in `Microsoft.ML.Mkl.Redist` for `linux-x64` (only `libMklImports.so` +
   `libMklProxyNative.so`; the system has GNU `libgomp`, which is ABI-incompatible). Verified:
   `ldd libMklImports.so` → `libiomp5.so => not found`, and the SSA unit tests threw
   `DllNotFoundException: MklImports` at the fit. The earlier packaging probe verified *compilation*,
   not the *runtime* fit.

So shipping ML.NET SSA would require an extra native lib (install/symlink LLVM `libomp5` as `libiomp5`
in dev **and** CI) — a fragile, non-$0/hermetic setup step, against the project's hard discipline.

## Decision

Implement demand forecasting as **pure-C# Holt-Winters** (additive triple exponential smoothing —
level + trend + weekly seasonality), in `Retail.Ml/Forecasting/`, with results written as DB rows.

- **`HoltWintersForecaster`** (`IDemandForecaster`): additive level/trend/seasonal recurrences
  (defaults α 0.3 / β 0.1 / γ 0.3, season 7); a flat-mean fallback when the series is shorter than two
  seasons. Deterministic, **no native dependencies**.
- **Prediction band** from the in-sample one-step residual σ, widening with the horizon (`± z·σ·√h`,
  z ≈ 1.2816 for an 80% interval); the 14-day-**total** band is quadrature-propagated and clamped ≥ 0
  (`ForecastMath`, the pure + fully-covered home of that math).
- **`Forecast:Mode`** seam (`hw` default | `stub`), mirroring `Ai:Mode`'s config-selected-impl
  mechanism (but real-by-default — no key needed).
- **In-process** `ForecastService` + daily `ForecastRefreshHostedService` write the rows; a
  `Retail.Ml.Trainer` CLI reuses the same `ForecastService.RefreshAsync` for a manual recompute, and a
  build-only `ml-train.yml` proves the offline-pipeline shape. No Azure, no cron execution.

## Consequences

**Positive**
- **$0, keyless, hermetic, dependency-free.** Runs identically on dev, CI, tests, and the demo with no
  native runtime, no key, no Azure — fully honoring the project discipline.
- **Deterministic + fully testable.** Pure C# → exact unit tests (a flat series recovers the level, a
  trend projects upward) and a covered 85%-gate footprint (no `[ExcludeFromCodeCoverage]` glue).
- **Interview-defensible end-to-end.** Level/trend/seasonal smoothing, zero-fill, the residual-σ band,
  the quadrature total-band propagation, and the safety-stock formula all fit on a whiteboard. Honest
  framing: "implemented + de-scoped ML.NET SSA after a runtime native-dependency blocker, pivoted to a
  managed model" is itself a credible engineering-judgment talking point.

**Negative / trade-offs**
- **Not ML.NET.** The résumé line is "time-series demand forecasting (Holt-Winters)", not "ML.NET SSA".
  ML.NET-the-library isn't itself the differentiator; the forecasting + pipeline are.
- **Univariate, classical.** No promo/price regressors; captures level + trend + weekly seasonality
  only (REQUIREMENTS limitation, same as the SSA plan would have had at this scope).
- **Simple band/safety-stock under intermittent demand.** The residual-σ band + `1.65·σ·√7` safety
  stock use a normal approximation that mis-fits lumpy demand; documented (PHASE_5B_FORECAST_SCOPE §17),
  a Croston-style method is the honest upgrade.

## Alternatives considered

1. **ML.NET SSA + provision `libiomp5` in dev & CI** (install LLVM `libomp5`, symlink `libiomp5 →
   libomp.so.5).** Rejected: a fragile native setup step in two environments, against the $0/hermetic
   rule. (Comparable-weight to the Docker/Testcontainers CI dep, but it buys little over a managed
   model and keeps a deprecated-feeling native dependency around.)
2. **Managed default + SSA opt-in behind `Forecast:Mode=ssa`.** Rejected for 5B: most code, and the SSA
   path would stay untested in CI (no MKL) — a permanently-unexercised branch.
3. **Azure Blob `ModelStore` + serialized models** (the original §9.1/§9.2). Deferred to Phase 8 by the
   DB-rows/$0 decision; moot for Holt-Winters (it refits in-process from rows — nothing to serialize).

## Implementation notes

- `Retail.Ml/Forecasting/`: `DailySeriesBuilder` (zero-fill), `HoltWintersForecaster`,
  `StubDemandForecaster`, `ForecastMath` (clamp + quadrature), `IDemandForecaster`, result records.
- `Retail.Api/Services/ForecastService` builds each variant's 180-day daily series (grouped + zero-filled
  in memory — EF can't translate a day-of-`PlacedAt` grouping), forecasts, writes `DemandForecast` +
  upserts `ReorderHint`; cold-start/sparse variants are skipped.
- `Forecast:Mode` is plain config (no secret, no `ValidateOnStart`).

## Revisit triggers

- **A managed ML.NET SSA path (no MKL) ships, or the project's CI/runtime reliably provides `libiomp5`.**
  → Re-evaluate SSA; the `IDemandForecaster` seam makes it a drop-in.
- **Forecast accuracy (MAPE/RMSE backtest) proves inadequate.** → Tune smoothing params, add regressors,
  or adopt a stronger model behind the same seam.
- **A non-portfolio deployment needs Blob-published models / a real nightly trainer.** → Phase-8 the
  Blob `ModelStore` + a scheduled `ml-train.yml` + `ForecastRefreshFn`.
