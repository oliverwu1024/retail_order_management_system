# ADR-0003: Z-Score for Fraud Scoring — Not Azure Anomaly Detector

**Status**: Accepted (2026-06-06); **amended 2026-06-21** for the Phase-5B order-anomaly as-built (see the Amendment at the end).

**Deciders**: project owner

**Related**: PLAN.md §8 (AI features), §5 (Tech Stack — ML.NET) · REQUIREMENTS.md (Epic — Fraud scoring) · ADR-0005 (multi-provider abstraction parallels)

---

## Context

Phase 5 of the project ships four AI features. One is **transaction fraud scoring** — at order placement, flag transactions that are statistically unusual for the customer (or for the cohort, if the customer is new), so staff can review them before fulfilment.

The candidate approaches:

1. **Azure AI Anomaly Detector** (a Cognitive / Azure AI service). Univariate and multivariate APIs, time-series-oriented.
2. **ML.NET trainers** (e.g. `RandomizedPcaTrainer`, `SrCnnAnomalyDetector` for time-series).
3. **A statistical Z-score** computed against a per-customer rolling baseline, implemented in our own ML pipeline.
4. **Rule-based thresholds** (e.g. flag if amount > USD X).

Two facts make Azure AI Anomaly Detector a poor fit despite being the obvious "cloud-native" choice:

- **The service is being retired.** Microsoft has announced deprecation; new resource creation is restricted, and the service is scheduled for shutdown. Coupling a portfolio project — meant to outlive the build window and be defensible in interviews indefinitely — to a deprecated service is a permanent liability.
- **Latency and cost.** A network round-trip per checkout adds 50–200 ms and per-call billing. For a synchronous step in the order pipeline, that compounds.

The project owner's resume narrative wants ML / statistical content that is **defensible end-to-end in an interview** — i.e. "I can derive it on a whiteboard, explain the threshold choice, and show the training pipeline." Z-score gives that.

## Decision

Implement fraud scoring as a **per-customer Z-score on transaction amount**, with a **log transform** to handle the heavy-tailed amount distribution, and a **cohort fallback** for cold-start customers.

- Trainer (`Retail.Ml.Trainer`) computes per-customer baseline parameters (mean and stddev of `log(amount)` over a rolling window) from historical orders; persists to a `CustomerSpendingBaseline` table.
- Refresh cadence: nightly Azure Functions job rebuilds baselines from the prior 90 days of orders.
- Inference: synchronous, called from `OrderService.PlaceOrder()` after totalisation and before payment authorisation. Computes `z = (log(amount) - mu) / sigma`. Adds < 5 ms.
- Threshold (`Fraud:ZThreshold`, default `3.0`): below threshold = no flag; threshold to threshold+1 = soft flag (require email confirmation); above threshold+1 = hard flag (staff review queue).
- Cold start (fewer than 10 prior orders for the customer): fall back to a global cohort baseline computed over all customers in the same loyalty tier.
- Telemetry: `fraud.z_score`, `fraud.action`, `fraud.baseline_source` (`per_customer` | `cohort`) emitted as Serilog properties per scored transaction.
- Stub mode: `Fraud:Mode = stub` always returns `z = 0` (no flag) for offline / demo use.

## Consequences

**Positive**

- **No deprecated-service risk.** Z-score is forever. The pipeline is ours; the algorithm is textbook.
- **Interview-defensible.** "I implemented Z-score with a log transform on per-customer baselines, with cohort fallback for cold start" is a complete story whose math fits on a whiteboard.
- **No external API dependency.** Works offline; no per-call cost; no rate limit; no extra outage risk in the order pipeline.
- **Composes with the rest of the ML stack.** `Retail.Ml` houses the other analytics models; adding baselines is one more pipeline, not a new technology. _(As-built note: demand forecasting shipped as pure-C# Holt-Winters with no persisted/recommendation model — ADR-0012; this bullet was aspirational at write time.)_
- **Per-customer baseline is the right unit of normalisation.** A USD 500 transaction is unusual for a customer who averages USD 30 and normal for one who averages USD 400. Anomaly Detector's per-series mode would have given the same shape with more cost.

**Negative / trade-offs**

- **Univariate only** (amount). Catches amount-spike fraud; misses velocity- or geography-based fraud patterns. Mitigation: separate per-feature Z-scores for `velocity_24h`, `time_of_day_shift`, etc., combined via maximum (or weighted sum) of the per-feature Z-scores. Phase 5 ships amount-only; multivariate is a follow-on.
- **Distributional assumption.** Z-score assumes log-amounts are roughly normal. Heavy-tailed customers (occasional bulk orders) produce false positives. Mitigation: log transform mitigates the worst tail; threshold tuning + soft-vs-hard split absorbs the rest; staff feedback loop labels false positives for future model improvement.
- **Cold start.** New customers have no per-customer baseline. Mitigation: cohort fallback covers the first ~10 transactions; per-customer baseline kicks in after.
- **Concept drift.** Customer behaviour shifts over time (e.g. promoted to a new tier). Mitigation: 90-day rolling window; nightly refresh; tier change triggers a fresh baseline recomputation.

## Alternatives considered

1. **Azure AI Anomaly Detector (Cognitive Service)**
   - Rejected: service is being retired. Permanent demerit for a long-lived portfolio. Plus per-call latency and cost on a synchronous checkout step.

2. **ML.NET `RandomizedPcaTrainer` (unsupervised anomaly)**
   - Rejected: overkill for primarily univariate signal. PCA on 1–3 features is contrived; the trainer's hyper-parameters are harder to defend than "mean and stddev of log-amount". The story it tells an interviewer is muddier.

3. **ML.NET `SrCnnAnomalyDetector` (time-series)**
   - Rejected: time-series methods fit aggregate signals (orders per minute) rather than per-transaction outlier detection. Demand forecasting is a separate concern (and shipped as Holt-Winters, not an ML.NET time-series trainer — ADR-0012), not fraud.

4. **Isolation Forest via ONNX import or Python training**
   - Rejected: introduces a Python or ONNX-import pipeline whose complexity does not pay back at our feature count. Z-score covers the use case in two days.

5. **Rule-based thresholds (e.g. `amount > USD 500 → flag`)**
   - Rejected: not adaptive (fails for high-spending customers; fires constantly for low-spending ones), tells no ML story for the resume, and accumulates rule debt over time.

## Implementation notes

- `Retail.Ml/Fraud/` contains `BaselineTrainer.cs` and `ZScoreScorer.cs`.
- `CustomerSpendingBaseline` table: `(CustomerId PK, Mu, Sigma, SampleCount, WindowStart, WindowEnd, RefreshedAt)`. Cohort baselines stored with `CustomerId = NULL, TierId = X`.
- Nightly job: Azure Function with timer trigger (`0 0 3 * * *` UTC) calls `BaselineTrainer.RebuildAllAsync()`.
- `IFraudScorer` interface in `Retail.Api/Services/Fraud/`; implementation calls into `Retail.Ml` and returns a `FraudScoreResult { ZScore, Action, BaselineSource }`. `OrderService.PlaceOrderAsync` consumes it.
- Numerical safety: guard against `Sigma == 0` (single-amount customer) by falling back to cohort. Guard against `Sigma < 1e-6` (numerically degenerate) the same way.
- Determinism for tests: trainer accepts a `Random` seed; test fixtures seed it explicitly.
- A `StubFraudScorer` returns a constant zero; registered when `Fraud:Mode = stub`. Same interface — `IFraudScorer` — so services are unaware.

## Revisit triggers

- **False-positive rate measured (by staff feedback labels) exceeds 5%.** → Tune threshold; consider multivariate features (velocity, geography, device fingerprint).
- **A fraud pattern emerges that the amount-only score cannot catch** (e.g. many small transactions in short succession). → Add per-feature Z-scores; combine via max or weighted sum.
- **A first-party Microsoft or ML.NET anomaly trainer with strong defaults and good documentation ships** and is not deprecated. → Re-evaluate; Z-score's resume story remains intact whether we keep or replace it.
- **A non-portfolio version of this project goes to production** with regulatory or audit requirements (PCI, AML). → Replace with a proper fraud platform (Stripe Radar, third-party); Z-score is illustrative, not certifiable.

---

## Amendment (2026-06-21) — Order-anomaly detection as-built (Phase 5B)

Phase 5 was split; Phase 5B ships **order-anomaly detection** (REQUIREMENTS §10) — a sibling of the
fraud scoring this ADR describes. It **adopts the core decision above** (a Z-score on a log transform,
with the `σ == 0` / `σ < 1e-6` numerical-safety guard, and no Azure Anomaly Detector) but the
*delivery mechanism* is deliberately lighter than the fraud design in **Decision** / **Implementation
notes**. As-built:

- **Shared scorer, new home.** The pure scorer is `Retail.Ml/Anomaly/ZScoreScorer.cs`
  (`Score(value, sample)` → log-Z with the σ-guard), **not** `Retail.Ml/Fraud/`. It is the first real
  code in `Retail.Ml` and is reusable by the future fraud scorer.
- **No `CustomerSpendingBaseline` table, no nightly trainer.** A 15-minute `OrderAnomalyHostedService`
  computes each buyer's mean/σ **on the fly** from their recent paid orders (last ~50; `< 5` prior → a
  **global** baseline, in place of the loyalty-tier cohort). The baseline-rebuild Function is not built
  for order-anomaly.
- **Batch, not synchronous-at-checkout.** Detection is a recurring background scan over recent paid
  orders, plus an **evaluate-on-ship** guard — not an inline step in order placement.
- **Three rules, not amount-only.** Besides the amount Z-score it flags a never-seen shipping country
  and a per-line quantity spike (REQUIREMENTS §10.1).
- **No `Mode` flag.** Order-anomaly is pure in-process math with no external dependency, so there is no
  `stub` mode (unlike `Fraud:Mode = stub` above, or the Phase-4 sentiment adapter).
- **Output + action.** One `OrderAnomaly` row per flagged order → a back-office **Risk Queue**; a
  flagged, unacknowledged order is **blocked from Mark-Shipped** until acknowledged (REQUIREMENTS §10.2).

The original fraud design (per-customer `CustomerSpendingBaseline` + nightly trainer + synchronous
scoring at checkout, with soft/hard thresholds and loyalty-tier cohorts) **remains the plan for the
dedicated fraud-scoring feature**, which is not built in Phase 5B. See PHASE_5B_SCOPE §3.2 / §18.
