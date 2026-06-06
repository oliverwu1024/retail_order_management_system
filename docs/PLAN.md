# Retail Order Management System — Implementation Plan

> Approved 2026-06-05; **revised 2026-06-06** to work backwards from two specific resume role entries (see `docs/REQUIREMENTS.md` Epics 0–10 and the per-bullet north star). See also `docs/DATABASE_DESIGN.md` for the data model and `docs/CODING_STANDARDS.md` for code conventions.

## 1. Context

A greenfield Retail OMS built as portfolio + learning project. **2026-06-06 reframe**: instead of a generic MVP, the project is now built as *evidence material* for two distinct resume role entries (Job A backend/platform framing, Job B frontend/product framing). Every Epic in `docs/REQUIREMENTS.md` traces back to a specific resume bullet. The 4 AI features still ship as recruiter hooks even though they're not on either bullet — they're unique differentiators.

Success = deployed to Azure with a public URL, four AI surfaces visibly working, CI/CD green, codebase senior-engineer-skim-approval-worthy in 60 seconds, AND every claim in either resume role entry has a code artifact or measurement report defending it under interview drill-down.

**Target timeline: ~31–40 weeks (~7–9 months) over 11 phases.** Each phase is independently demo-able. This is up from the original ~8 weeks; the user explicitly accepted the longer timeline on 2026-06-06 in exchange for the comprehensive resume defense surface.

## 2. Locked-in Decisions

| Layer | Choice |
|---|---|
| Frontend | React 18 + Vite + TypeScript (single app: storefront + `/admin` route) |
| Styling | **Tailwind CSS + shadcn/ui (Radix-based headless)** — hand-built `components/ui/` library. *Flipped 2026-06-06 from Refine.dev + MUI v5 to match Job B "Tailwind CSS" bullet.* |
| Backend | ASP.NET Core 10 LTS (GA Nov 2025; support through Nov 2028). *Flipped from net8.0 on 2026-06-06 — see ADR-0006.* |
| API style | **MVC Controllers** with `[ApiController]` + attribute routing |
| Architecture | **Three-tier** in single `Retail.Api` project (Controllers → Services → Repositories → DbContext) |
| Auth | ASP.NET Core Identity + **JWT access token in HTTP-only / Secure / SameSite=Strict cookie + refresh-token rotation + CSRF double-submit token**. *Upgraded 2026-06-06 from localStorage.* Roles: `Customer`, `StoreManager`, `Staff`, `Administrator` |
| Database | SQL Server 2022 (Docker locally, Azure SQL Serverless GP_S in prod) |
| Containers | Docker + docker-compose for dev |
| CI/CD | GitHub Actions w/ OIDC federated identity to Azure. Required PR gates: lint + typecheck + unit tests + integration tests + build (4 must pass — drives 99%+ deployment success rate claim) |
| Cloud | Microsoft Azure — **Australia East (Sydney)** |
| API gateway | **Azure API Management (APIM)** in front of Container Apps *(added 2026-06-06)* |
| Backend hosting | Azure Container Apps (main API, min=0/max=2) |
| Event-driven | **Azure Service Bus (queues) + Event Grid + Azure Functions consumers** for async order confirmation, voucher redemption, Stripe webhook fan-out, scheduled jobs (points expiry, tier recalc) *(added 2026-06-06; reverses earlier "no Service Bus" decision)* |
| In-process background | Built-in `BackgroundService` for cart-expiry sweeper + review sentiment queue |
| Frontend hosting | Azure Static Web Apps (free tier) |
| Observability | Application Insights + Azure Monitor + Log Analytics + OpenTelemetry + **10+ runbooks in `docs/runbooks/`** *(expanded 2026-06-06)* |
| Perf/load testing | **k6 scripts in `tests/load/`**, baseline + post-optimization reports in `docs/perf/` *(added 2026-06-06)* |
| Code quality measurement | **Coverlet (coverage, 85% gate)** + **jscpd (duplication, before/after reports)** *(added 2026-06-06)* |
| AI #1 — Chatbot | **Custom React Tailwind drawer** (Phase 5) → ASP.NET webhook → Anthropic Claude API (tool use). **Copilot Studio bot** as Phase 6 stretch reusing same webhook contract. |
| AI #2 — Copy gen | Anthropic Claude API (admin-triggered) behind `ILlmClient` (ADR-0005) |
| AI #3 — Forecasting | ML.NET SSA time-series, nightly retrain |
| AI #4 — Sentiment + anomaly | Azure AI Language (sentiment) + Z-score in-process (anomaly) |
| Promotions (Phase 7) | Vouchers + Loyalty (Medium) + unified pricing pipeline |
| Payments | Stripe Checkout, **test mode forever** |

## 3. High-Level Architecture

```
                                +--------------------------+
                                |  Copilot Studio bot      |
                                |  (Phase 6 STRETCH, opt.) |
                                +-------------+------------+
                                              | HTTPS webhook (HMAC)
                                              v
+-------------------+    HTTPS    +--------------------+    +---------------------------------------+
| Browser           | <---------> |  Azure API         | -> |  Azure Container Apps                 |
|  - Storefront     |  HTTP-only  |  Management (APIM) |    |  --------------------                 |
|    + chat drawer  |  cookie     |  (rate limit,      |    |  ASP.NET Core 10 LTS Web API          |
|  - Admin (/admin) |  + CSRF     |   JWT validate,    |    |   - Catalog / Cart / Orders / Vouchers|
|  React 18 + Vite  |             |   OpenAPI docs)    |    |   - Loyalty / Pricing pipeline        |
|  Tailwind + shadcn|             +--------------------+    |   - Auth (Identity + JWT-in-cookie)   |
|  hosted on:       |                                       |   - AI controllers (Claude, AI Lang)  |
|  Azure Static     |                                       |   - Chat webhook                      |
|  Web Apps         |                                       |   - In-process BackgroundServices:    |
                                                            |       * CartExpirySweeper             |
                                                            |       * ReviewSentimentProcessor      |
                                                            +---+-----+------+-------+-----+--------+
                                                                |     |      |       |     |
                                                       EF Core  |     | publish events  | HTTPS
                                                                v     |      |       |     v
                                                       +------------+ |      |       | +-------------+
                                                       | Azure SQL  | |      |       | | Anthropic   |
                                                       | Serverless | |      |       | | Claude API  |
                                                       | (auto-     | |      |       | +-------------+
                                                       |  pause)    | |      |       |
                                                       +------------+ |      |       | +-------+
                                                                      v      v       v | Azure |
                                                            +---------+ +---------+ +---+ AI    |
                                                            | Azure   | | Azure   |     | Lang. |
                                                            | Service | | Event   |     | F0    |
                                                            | Bus     | | Grid    |     +-------+
                                                            | (queues)| | (topics)|
                                                            +----+----+ +----+----+
                                                                 |           |
                                                                 v           v
                                                       +---------------------------+
                                                       | Azure Functions           |
                                                       |  - StripeWebhookHandler   |
                                                       |  - OrderConfirmationFn    |
                                                       |  - VoucherRedemptionFn    |
                                                       |  - PointsExpiryScheduled  |
                                                       |  - TierRecalcScheduled    |
                                                       |  - ForecastRefreshFn      |
                                                       |  - OrderAnomalyScanFn     |
                                                       +---------------------------+

                                            +-------+
                                            | Azure |
                                            | Blob  |
                                            | Stor- |
                                            | age   |
                                            +-------+
                                            | imgs +|
                                            |ML mdl |
                                            +-------+

                                            +-----------------------+
                                            | Azure Key Vault       | (Managed Identity)
                                            +-----------------------+

   +------------------+   ACR   +-------------------------+
   | GitHub Actions   | ------> | Azure Container Registry|
   | (OIDC -> Azure)  |         +-------------------------+
   +------------------+

   +----------------------+   OTel   +-------------------------+
   | Application Insights | <------- | API + Functions + APIM  |
   | + Azure Monitor      |          | + SWA client telemetry  |
   | + Log Analytics      |          +-------------------------+
   +----------------------+

   Stripe (test mode) ---webhook---> APIM ---> Event Grid ---> StripeWebhookHandler Function
```

**Hosting choices**:
- **Frontend** → Azure Static Web Apps (free tier; built-in CDN; routes `/api/*` proxy to APIM).
- **APIM** → Consumption tier (pay-per-call, fits min=0 story).
- **Backend API** → Azure Container Apps, **min-replicas=0** so it scales to zero when idle (~$0 cost when not demoing).
- **Async work** → Service Bus queues + Event Grid topics, consumed by Azure Functions (Consumption plan).
- **ML training/inference** → in-process inside the API as `BackgroundService` + `IMemoryCache` for predictions, plus a `ForecastRefreshFn` Azure Function for the nightly batch.

## 4. Repo Structure (monorepo, three-tier backend)

Backend uses **three-tier architecture** in a single API project: `Controllers/` → `Services/` → `Repositories/` → `Data/`. `Retail.Api`, `Retail.Ml`, `Retail.Ml.Trainer`, `Retail.Functions`, and test projects are separate. See `docs/CODING_STANDARDS.md` § Project Structure for the full layout.

```
retail_order_management_system/
  src/
    api/                              # ASP.NET Core 10 LTS solution (three-tier)
      Retail.Api/                     # Single API project — Controllers/Services/Repositories/Domain/Data
      Retail.Functions/               # Azure Functions (Stripe webhook, Event Grid subscribers, scheduled jobs)
      Retail.Ml/                      # ML.NET training + prediction services
      Retail.Ml.Trainer/              # CLI for GitHub Actions scheduled retrains
      Retail.Tests.Unit/              # xUnit, fast, no DB
      Retail.Tests.Integration/       # xUnit + WebApplicationFactory + Testcontainers SQL + Service Bus emulator
      Retail.sln
    web/                              # Vite + React + TS + Tailwind + shadcn/ui
      src/
        app/                          # router, providers
        components/
          ui/                         # 12+ reusable shadcn-style primitives (Button, Input, DataTable, ...)
          layouts/                    # AdminShell, StorefrontShell
        features/
          storefront/                 # catalog, cart, checkout, account
          admin/                      # dashboard, orders, products, inventory, vouchers, loyalty, sessions
          auth/                       # login, signup, profile, password
          chat/                       # custom React chat drawer (Phase 5)
        lib/                          # generated API client, zustand stores, csrf helper
      e2e/                            # Playwright golden-path tests
      package.json
      tailwind.config.ts
      vite.config.ts
    chatbot/                          # Phase 6 stretch
      copilot-studio-export.zip
      README.md
  infra/
    bicep/
      main.bicep
      modules/
        apim.bicep
        containerApps.bicep
        sql.bicep
        keyVault.bicep
        storage.bicep
        ai.bicep
        monitoring.bicep
        registry.bicep
        staticWebApp.bicep
        serviceBus.bicep
        eventGrid.bicep
        functions.bicep
      env/
        dev.bicepparam
        prod.bicepparam
  docker/
    docker-compose.yml                # api + sql + azurite + servicebus-emulator + functions-host
    sql/init.sql
  tests/
    load/                             # k6 scripts (baseline + scenarios for p95, query perf, events/day)
      catalog-browse.js
      checkout-flow.js
      voucher-redemption.js
  .github/
    workflows/
      ci.yml
      cd-staging.yml
      cd-prod.yml
      iac.yml
      ml-train.yml
      load-test.yml                   # nightly k6 against staging, posts results to artifact
  docs/
    PLAN.md                           # this file
    REQUIREMENTS.md                   # functional + non-functional requirements (Chinese-primary PRD)
    CODING_STANDARDS.md               # C#/TS/SQL/Git/Tailwind conventions
    DATABASE_DESIGN.md                # ER diagram + per-table contracts
    architecture.md                   # rendered diagrams + walkthrough
    contracts/chat-webhook.md
    perf/                             # k6 baseline + optimization reports
      baseline-2026-XX.md
      post-optim-2026-XX.md
    runbooks/                         # 10+ operational runbooks (target: A-5)
      api-5xx-spike.md
      stripe-webhook-failure.md
      service-bus-dead-letter.md
      ...
    adr/
      0001-net8-vs-net9.md
      0002-no-mediatr.md
      0003-zscore-not-anomaly-detector.md
      0004-mvc-controllers-over-minimal-apis.md
      0005-multi-provider-llm.md
      0006-tailwind-vs-mui-flip.md           # added 2026-06-06
      0007-jwt-cookie-vs-localstorage.md     # added 2026-06-06
      0008-event-driven-promotion-pipeline.md
      0009-apim-vs-direct-container-apps.md
    screenshots/
  README.md
  .editorconfig
  .gitignore
  Directory.Build.props
```

Monorepo over polyrepo: one PR can change contract + client + IaC together; CI regenerates the typed API client from OpenAPI and fails on drift.

## 5. Tech Stack Specifics

**Backend**
- **.NET 10 LTS** — GA Nov 2025, supported through Nov 2028. *Flipped from .NET 8 LTS on 2026-06-06 — see ADR-0006.*
- **EF Core 10** (SQL Server provider). Migrations in `Retail.Api/Data/Migrations/`.
- **ASP.NET Core Identity 10** + **JWT bearer**. Access token issued into **HTTP-only / Secure / SameSite=Strict cookie**; refresh token rotated on every refresh; CSRF double-submit token on state-changing endpoints. ADR-0007 explains the choice over localStorage.
- **API style**: MVC Controllers with `[ApiController]` + attribute routing.
- **Architecture**: classic three-tier — Controller → Service → Repository → DbContext. Service is the only layer that may start multi-table transactions and call external clients.
- **FluentValidation 11** auto-wired.
- **No MediatR** (paid since Sept 2025).
- **Serilog 4.x** → Console (dev), Application Insights sink (prod).
- **OpenTelemetry** via `OpenTelemetry.Extensions.Hosting` + `OpenTelemetry.Exporter.AzureMonitor`.
- **`Microsoft.Extensions.Http.Resilience`** (Polly 8) on all outbound HTTP clients.
- **Background jobs**: mixed — `BackgroundService` for in-process loops; **Azure Service Bus + Azure Functions** for cross-service async work.
- **Azure SDKs**: `Azure.Messaging.ServiceBus`, `Azure.Messaging.EventGrid`, `Azure.AI.TextAnalytics`, `Azure.Storage.Blobs`, `Azure.Security.KeyVault.Secrets`, `Azure.Identity` (`DefaultAzureCredential` everywhere).
- **Stripe.NET 47.x** for Checkout + webhook signature verification.
- **ML.NET 4.x** (`Microsoft.ML` + `Microsoft.ML.TimeSeries`) for SSA forecasting.
- **Anthropic.SDK** behind `ILlmClient` (ADR-0005). Services never `using Anthropic.SDK`.
- **OpenAPI**: `Swashbuckle.AspNetCore` (Swagger UI in non-prod, exported as OpenAPI 3.0 to APIM in prod).

**Frontend**
- **React 18.3 + Vite 5 + TypeScript 5.6**.
- **Tailwind CSS 3.4** + **shadcn/ui** (Radix-based headless components, copied into repo at `components/ui/`).
- **TanStack Query v5** + **Zustand 4**.
- **React Router v6.4+ data router**.
- **Forms**: React Hook Form + Zod.
- **API client**: `openapi-typescript` + `openapi-fetch` from OpenAPI doc. CSRF token attached to every state-changing request from a non-HTTP-only cookie.
- **Charts**: Recharts (replaces MUI X Charts).
- **a11y**: Radix primitives are accessible by default; Playwright a11y assertions via `@axe-core/playwright`.

**Component library deliverable** (Job B-1 target):
12+ primitives under `src/web/src/components/ui/`: `Button`, `Input`, `Select`, `Checkbox`, `Modal`, `Drawer`, `DataTable`, `FilterPanel`, `Pagination`, `Toast`, `Tabs`, `Card`, `EmptyState`. Each documented in a `<Name>.stories.tsx` (Storybook optional). Composed throughout storefront + admin.

**Database**
- **SQL Server 2022** in compose for dev.
- **Azure SQL Database — Serverless GP_S_Gen5_1**, 1 vCore min, **auto-pause after 60 min**.

**Local tooling**
- Docker Desktop + docker-compose v2.
- **Azurite** for Blob locally.
- **Azure Service Bus emulator** (or in-process queue stub) for local async testing.
- **Azure Functions Core Tools v4** for local Functions host.
- **stripe-cli** for webhook forwarding.
- **.NET user-secrets** for dev-only API secrets.

**Quality & perf tooling**
- **Coverlet** for coverage (85% gate enforced in `ci.yml`).
- **jscpd** for code duplication detection — baseline before component-library refactor, after, both committed.
- **k6** for load testing — scripts in `tests/load/`, nightly via `load-test.yml`, results uploaded as artifacts.

## 6. Domain Model

(Full per-table column contracts in `docs/DATABASE_DESIGN.md`.)

All business entities have UTC audit fields (`CreatedAt`, `UpdatedAt`, `CreatedBy`, `UpdatedBy`). Soft delete on `Product`, `Category`, `Review`.

| Entity | Key fields |
|---|---|
| `AppUser` (`IdentityUser<Guid>`) | `Email`, roles (4: Customer / StoreManager / Staff / Administrator) |
| `RefreshToken` | `Hash`, `UserId`, `ExpiresAt`, `RevokedAt?`, `ReplacedByHash?` |
| `CustomerProfile` | `AppUserId`, `DisplayName`, `Phone`, list<`Address`> |
| `Address` | `Line1/2`, `City`, `Region`, `PostalCode`, `Country`, default flags |
| `Category` | `Slug`, `Name`, `ParentId?` |
| `Product` | `Sku`, `Slug`, `Name`, `Description`, `Seo*`, `CategoryId`, `BrandName`, `IsPublished` |
| `ProductVariant` | `Sku`, `OptionsJson`, `PriceCents`, `CompareAtPriceCents?`, `IsActive` |
| `InventoryItem` (1:1 variant) | `OnHand`, `Reserved`, `RowVersion` |
| `InventoryReservation` | `CartId?`, `OrderId?`, `Quantity`, `ExpiresAt`, `Status` |
| `Cart` | `CustomerProfileId?`, `AnonymousKey?`, `Status`, `ExpiresAt` |
| `CartItem` | `ProductVariantId`, `Quantity`, `UnitPriceCentsSnapshot` |
| `Order` | `OrderNumber`, `Status`, money cents, address JSON snapshots, `PlacedAt`, `RowVersion` |
| `OrderLine` | `ProductVariantId`, `Quantity`, snapshots |
| `OrderPriceBreakdown` *(new)* | `OrderId`, snapshot of pricing pipeline result (subtotal, voucher discount, loyalty discount, shipping, tax, total) |
| `Payment` | `Provider`, `StripeSessionId`, `StripePaymentIntentId`, `AmountCents`, `Status`, `RawPayloadJson` |
| `Shipment` | `Carrier?`, `TrackingNumber?`, `Status`, `ShippedAt?`, `DeliveredAt?` |
| `Review` | `ProductId`, `Rating`, `Body`, `SentimentScore`, `SentimentLabel`, `ProcessedAt?` |
| `AuditLog` | `Actor`, `Action`, `EntityType`, `EntityId`, `BeforeJson?`, `AfterJson?`, `OccurredAt` |
| `Voucher` *(new)* | `Code`, `Type` (PercentOff / AmountOff / FreeShipping), `Value`, `MinSpendCents?`, `MaxTotalUses`, `MaxPerCustomer`, `UsesRemaining` (rowversion), `StartsAt`, `ExpiresAt`, `IsActive` |
| `VoucherRedemption` *(new)* | `VoucherId`, `OrderId`, `CustomerProfileId`, `AppliedDiscountCents`, `RedeemedAt` |
| `LoyaltyAccount` *(new)* | `CustomerProfileId` (unique), `Balance` (computed), `LifetimePointsEarned`, `Tier` (Bronze/Silver/Gold), `TierEvaluatedAt` |
| `LoyaltyTransaction` *(new)* | `LoyaltyAccountId`, `Kind` (Earn/Redeem/Expire/Adjust), `Points`, `OrderId?`, `Reason`, `OccurredAt`, `IdempotencyKey` (unique) |
| `DemandForecast` | `ProductVariantId`, `Horizon`, `ForecastedQty`, `LowerBound`, `UpperBound`, `Confidence`, `ModelVersion` |
| `ReorderHint` | `ProductVariantId`, `RecommendedOrderQty`, `Reasoning`, `Dismissed` |
| `OrderAnomaly` | `OrderId`, `Score`, `Reason`, `Acknowledged` |
| `ChatSession` | `CustomerProfileId?`, `ConversationId`, `StartedAt`, `LastMessageAt` |
| `ChatMessage` | `ChatSessionId`, `Role`, `Content`, `ToolName?`, `ToolPayloadJson?` |
| `ProcessedStripeEvent` | `EventId`, `ReceivedAt` — webhook idempotency |

## 7. API Surface (MVC Controllers under `/api/v1`)

```
/api/v1/auth          register, login, refresh, logout, me, csrf-token
                      (login/refresh set HTTP-only cookie; logout clears it)
/api/v1/catalog       GET products (filter/page/sort), GET product/{slug}, GET categories
                      ADMIN: POST/PUT/DELETE products, POST variants, POST image,
                             POST products/{id}/generate-copy
/api/v1/inventory     STAFF+: GET items?lowStock, POST items/{variantId}/adjust,
                              GET reorder-hints, POST reorder-hints/{id}/dismiss
/api/v1/cart          GET, POST items, PATCH items/{id}, DELETE items/{id}, POST reserve,
                      POST apply-voucher, DELETE apply-voucher, POST apply-loyalty
/api/v1/checkout      POST session  (creates Stripe Checkout with priced cart)
/api/v1/payments      POST stripe/webhook (routes via Event Grid → Function)
/api/v1/orders        GET, GET {id}, POST {id}/cancel, POST {id}/refund,
                      POST {id}/ship, GET {id}/anomalies, GET {id}/breakdown
/api/v1/vouchers      ADMIN: GET, POST, PATCH, DELETE
                      ADMIN: GET {code}/usage (stats)
                      PUBLIC: POST validate (called from cart UI)
/api/v1/loyalty       CUSTOMER: GET me (account + recent txns)
                      ADMIN: GET accounts, GET {customerId}/ledger, POST {customerId}/adjust
                      ADMIN: GET tiers, PUT tiers (manage thresholds)
/api/v1/reviews       POST products/{id}/reviews, GET products/{id}/reviews
/api/v1/chat          POST webhook, GET sessions/{id}/history (admin)
/api/v1/analytics     STAFF+: GET forecast, GET sales-summary, GET sentiment-summary,
                              GET anomalies, GET vouchers-usage, GET loyalty-summary
/health/live, /health/ready, /health/startup
```

All non-public endpoints require JWT (delivered via HTTP-only cookie). `Idempotency-Key` header on POSTs that move money/inventory/points. `If-Match` / ETag on PUTs to entities with `RowVersion`. Pagination via `?page=&pageSize=`. APIM enforces rate limits + JWT validation before forwarding to Container Apps.

## 8. AI Feature Implementations

### 8a. Customer Support Chatbot
- **Phase 5 MVP**: custom Tailwind `Drawer` in storefront. Sends user turn + `conversationId` to `/api/v1/chat/webhook` (JWT cookie auth). Backend loads/creates `ChatSession`, pulls last 5 orders + 10 recent lines (RAG-lite), calls Claude with system prompt + **tool definitions** (`get_order`, `list_my_recent_orders`, `get_shipping_status`, `start_return`, `get_my_loyalty_balance`, `list_my_vouchers`), persists turn, returns text.
- **LLM call** goes through `ILlmClient.CompleteAsync()` (ADR-0005).
- **Phase 6 stretch**: Copilot Studio bot calls the same webhook with HMAC.
- **Prompt caching** on stable system prompt.
- **Failure mode**: Anthropic 5xx/timeout → HTTP 200 with friendly retry message. Polly 3-retry exp backoff, 30s cap.

### 8b. Product Description / SEO Copy Generation
- `POST /api/v1/catalog/products/{id}/generate-copy` → `CopyGenService`.
- Tool-forced JSON output (`emit_product_copy`). Never auto-save.

### 8c. Demand Forecasting (ML.NET SSA, nightly retrain)
- Per-variant 14-day forecast with safety-stock-based reorder hints.
- Nightly retrain via `ml-train.yml` + `ForecastRefreshFn` Azure Function.

### 8d. Sentiment + Order Anomaly
- **Sentiment**: `ReviewSentimentHostedService` consumes a `Channel<Guid>` (in-process, fast).
- **Anomaly**: `OrderAnomalyHostedService` every 15 min — Z-score on customer's last 50 order totals, new shipping country, > 5 of one variant in one order.

## 9. Payments & Inventory

**Payments**: Stripe Checkout in test mode (forever).
1. `POST /checkout/session` → run pricing pipeline (voucher + loyalty + tax + shipping), reservations (15-min), `Payment` in `Created`, Stripe Checkout Session.
2. Customer pays on Stripe-hosted page.
3. Stripe sends webhook → APIM → Event Grid topic → `StripeWebhookHandlerFn` (Azure Function).
   - Function verifies signature, checks idempotency via `ProcessedStripeEvent`, then publishes domain event (`OrderConfirmed`) to Service Bus.
   - `OrderConfirmationFn` consumer commits reservations, creates `Order` + lines + `OrderPriceBreakdown`, marks cart `Converted`.
   - For `charge.refunded`, reverses inventory and writes loyalty `Adjust` transaction to claw back unearned points.
4. **Loyalty points earned on order *ship*, not pay**, by a Service Bus message published when order transitions to `Shipped` (refund-safe). Earn calc: 1pt per AUD on `OrderPriceBreakdown.SubtotalCents`.

**Inventory**: `InventoryItem.RowVersion`. Decrement via `Where(i => i.RowVersion == original).ExecuteUpdateAsync(...)`. `CartExpirySweeper` (in-process `BackgroundService`) releases stale reservations every 5 min.

## 9.5 Promotions Pipeline (Phase 7)

**Pricing pipeline order** (applied at `/api/v1/checkout/session`):
```
subtotal = Σ(line.unit_price × qty)
  → minus voucher discount (one voucher per order, strategy pattern by Type)
  → minus loyalty redeem discount (100 pts = $1 off, capped at remaining subtotal)
  → plus shipping (free-shipping voucher zeros this)
  → plus tax (10% GST on post-discount subtotal)
  = total
```

Result snapshot persisted as `OrderPriceBreakdown` row at order placement (audit + reproducibility).

**Voucher** (strategy-pattern discount types): `PercentOff`, `AmountOff`, `FreeShipping`. Constraints: `MinSpendCents`, `StartsAt`/`ExpiresAt`, `MaxTotalUses` (optimistic concurrency on `UsesRemaining`), `MaxPerCustomer` (checked via `VoucherRedemption` query).

**Loyalty (Medium scope)**:
- `LoyaltyAccount` 1:1 `CustomerProfile`. Balance is a *computed* sum over `LoyaltyTransaction` rows — **never mutated directly** (double-entry ledger pattern).
- `LoyaltyTransaction.IdempotencyKey` unique — prevents double-award on Service Bus message redelivery.
- Tiers (Bronze < $0 lifetime, Silver ≥ $500, Gold ≥ $2000), rolling 12-month spend. `TierRecalcScheduled` Function runs nightly.
- Earn rule: 1 pt / $1 on `subtotalCents`, awarded on order ship.
- Redeem rule: 100 pts = $1 off, capped at remaining subtotal after voucher.
- Expiry: 12 months of inactivity → `Expire` transaction zeros balance. Run by `PointsExpiryScheduled` Function nightly.

**Out of scope (Phase 7)**: BOGO, referral codes, point multipliers, birthday bonuses, tier perks beyond redeem-rate parity, gift cards, store credit.

## 10. Azure Resources (Australia East)

Resource group naming: `rg-rom-{env}-australiaeast`.

| Purpose | Resource | SKU | Idle cost |
|---|---|---|---|
| API gateway | Azure API Management | Consumption | ~$0 |
| API | Azure Container Apps | Consumption, min=0/max=2, 0.5 vCPU / 1 GiB | ~$0 |
| Frontend | Azure Static Web Apps | Free | $0 |
| Async messaging | Azure Service Bus | Basic (queues only) | ~$0.05/mo |
| Event routing | Azure Event Grid | Pay-per-event | <$1/mo |
| Functions | Azure Functions | Consumption (Y1) | $0 |
| Database | Azure SQL Database | GP_S_Gen5_1 Serverless, auto-pause 60min | $0 paused |
| Registry | Azure Container Registry | Basic | ~$5/mo |
| Blob | Azure Storage | StorageV2, LRS, Hot | <$1/mo |
| AI Language | Azure AI Language | Free F0 (5k tx/mo) | $0 |
| Secrets | Azure Key Vault | Standard | ~$0.03/mo |
| Observability | App Insights + Log Analytics | PAYG, 1 GB/day cap | <$5/mo |

Budget alert: **$30/mo** on the resource group (raised from $25 to absorb APIM/Service Bus/Functions). **IaC**: Bicep modules listed in §4.

All app→Azure auth via system-assigned Managed Identity + `DefaultAzureCredential`.

## 11. CI/CD (GitHub Actions, OIDC)

**Required PR gates** (all must pass — drives 99%+ deployment success rate claim, Job B-4):
1. lint (`dotnet format --verify-no-changes` + `pnpm lint --max-warnings 0`)
2. typecheck (`pnpm tsc --noEmit`)
3. unit tests (`dotnet test --filter Category=Unit` + `pnpm vitest run`)
4. integration tests (`dotnet test --filter Category=Integration` w/ Testcontainers)
5. build (`dotnet build -c Release` + `pnpm build`)

| Workflow | Trigger | Does |
|---|---|---|
| `ci.yml` | PR + push to `main` | Run all 5 PR gates. Swagger ↔ TS client drift check. Coverlet coverage report (fail below 85%). jscpd duplication check (warn over threshold). |
| `cd-staging.yml` | push to `main` (after ci) | OIDC → ACR push (api + functions images) → `az containerapp update` + `az functionapp deployment` → smoke `/health/ready` |
| `cd-prod.yml` | manual dispatch + env approval | Re-tag staging image to `:prod`, deploy, smoke |
| `iac.yml` | changes to `infra/**` | `bicep what-if` on PR; deploy on merge (dev) / manual (prod) |
| `ml-train.yml` | cron `0 7 * * *` UTC | Train SSA models, push to Blob with manifest |
| `load-test.yml` | nightly cron + manual | Run k6 against staging, upload results as artifact, post summary to PR comment if comparing against baseline |

Deployment success tracked via GH API; weekly script in `tests/load/` aggregates success/total ratio for the resume claim.

## 12. Local Development

`docker/docker-compose.yml` services: `api`, `web`, `sqlserver`, `azurite`, `functions-host`, `servicebus-emulator` (community emulator or in-process stub for tests).

```bash
cp .env.example .env                                                    # 1. fill Anthropic + Stripe test keys
docker compose --profile init up sqlserver azurite servicebus -d \
  && (cd src/api/Retail.Api && dotnet ef database update) \
  && docker compose up -d                                               # 2. brings up api + functions + web
stripe listen --forward-to localhost:5080/api/v1/payments/stripe/webhook # 3 (separate terminal)
```

## 13. Phased Delivery Plan

Each phase is **independently demo-able**. Weeks are best-case targets; ~31–40 total weeks is realistic.

### Phase 0 — Foundations (Weeks 1–2)
- Repo skeleton per §4, `Directory.Build.props`, `.gitignore`, `.editorconfig`.
- 4 docs shipped (PLAN / REQUIREMENTS / CODING_STANDARDS / DATABASE_DESIGN).
- `Retail.Api` solution + projects (including `Retail.Functions`).
- Vite web app with **Tailwind + shadcn/ui** scaffolded; first 4 `components/ui/` primitives (`Button`, `Input`, `Card`, `Toast`).
- `docker-compose.yml` brings up api + sql + azurite + servicebus emulator + functions host.
- `infra/bicep/main.bicep` validates with `bicep build` (all module placeholders including apim, serviceBus, eventGrid, functions).
- `ci.yml` green with all 5 PR gates.
- ADRs 0001–0009 written (including 0006 Tailwind flip, 0007 cookie auth, 0008 event-driven promotions, 0009 APIM).

**Demo**: `docker compose up` → `/admin` placeholder loads with Tailwind styling, Swagger lists `/health/*`. CI badge green.

### Phase 1 — Catalog & Auth (Weeks 3–4)
- `Product`, `ProductVariant`, `Category`, `InventoryItem`, `RefreshToken` entities + EF config + migrations.
- ASP.NET Core Identity + JWT, **issued as HTTP-only cookie** (login/refresh/logout endpoints set/clear cookie; CSRF token endpoint).
- 4 roles seeded (`Customer`, `StoreManager`, `Staff`, `Administrator`).
- Customer self-signup. Default admin seeded from `appsettings`.
- Catalog endpoints (public reads, admin writes). Image upload to Azurite/Blob.
- Storefront: catalog grid with filter/search, product detail. Admin: pre-component-library product list + create form.
- Component library grows: add `Select`, `Checkbox`, `Modal`, `DataTable`, `FilterPanel`, `Pagination`.
- **First perf baseline**: k6 catalog browse run, recorded in `docs/perf/baseline-{date}.md`.

**Demo**: Register a customer (cookie issued), admin creates a product with image, image renders on storefront.

### Phase 2 — Cart & Orders (Weeks 5–6)
- `Cart`, `CartItem`, `InventoryReservation`, `Order`, `OrderLine`, `Payment`, `ProcessedStripeEvent`, `OrderPriceBreakdown` (subtotal/tax/shipping only — voucher/loyalty zero in Phase 2).
- Cart endpoints + storefront cart Drawer.
- Stripe Checkout session creation. **Stripe webhook now routes through APIM → Event Grid → `StripeWebhookHandlerFn` (Azure Function)** rather than direct to API — Phase 8 will fully wire Service Bus consumers, but the webhook landing is a Function from day 1 so the architecture is right.
- Inventory decrement with `RowVersion`. `CartExpirySweeper`.
- xUnit integration tests on inventory concurrency (Testcontainers).

**Demo**: Add to cart → Stripe test card → webhook → order shows; inventory drops; audit log written.

### Phase 3 — Admin Ops, Audit, 3-Role RBAC (Weeks 7–8)
- **Hand-built admin shell** (Tailwind `AdminShell` layout + sidebar nav + topbar — replaces Refine).
- Order list / detail / fulfillment (mark shipped, refund) using `DataTable`, `FilterPanel`, `Modal`, `Drawer` from component library.
- `AuditLog` viewer.
- **3-role RBAC matrix** wired in API (policies) and UI (route guards): `Administrator` (all), `StoreManager` (everything `Staff` plus user mgmt + refund + reports), `Staff` (fulfillment, inventory adjust, risk queue).
- Basic reports: sales-by-day.
- Vitest + Playwright golden-path E2E (with `@axe-core/playwright` a11y asserts).

**Demo**: Admin/StoreManager/Staff each log in to see different sidebar items; fulfill an order; refund (reverses inventory + audit).

### Phase 4 — AI: Copy Gen + Sentiment (Weeks 9–10)
- `CopyGenService` + admin "Suggest Description" flow.
- `Review` entity + customer-submit endpoint + product review list.
- Azure AI Language sentiment via `ReviewSentimentHostedService`.
- Admin sentiment summary tile + "Products Needing Attention" panel.

**Demo**: AI copy generated for 3 products; reviews → sentiment scores populate.

### Phase 5 — AI: Chatbot + Forecasting + Anomaly (Weeks 11–13)
- Custom Tailwind `ChatDrawer` (Headless UI/Radix Dialog + message list + input).
- `/api/v1/chat/webhook` (cookie auth + CSRF).
- Claude tool calls including `get_my_loyalty_balance`, `list_my_vouchers` (stubbed until Phase 7).
- `ChatSession`/`ChatMessage` persistence.
- ML.NET SSA forecasting + nightly training via `ml-train.yml`.
- `DemandForecast` + `ReorderHint` + dashboard tile.
- Order anomaly Z-score job (in-process `BackgroundService` for now; moved to a Function in Phase 8).
- Demo data seeder: 6 months of synthetic orders.

**Demo**: Customer chat asks "where is my last order?" → tool-call → tracking. Forecast tile + risk queue work.

### Phase 6 — Azure Deploy + APIM + Basic Observability (Weeks 14–16)
- `cd-staging.yml` + `cd-prod.yml` end-to-end with OIDC.
- Bicep deploys everything to fresh subscription: APIM, Container Apps, Functions, Service Bus, Event Grid, SQL, Storage, AI Language, Key Vault, ACR, SWA, App Insights.
- App Insights basic dashboard.
- Demo data seeded in staging.
- README with architecture diagram, screenshots, ADR index.
- Cost guardrails documented.
- **First component-library dedup pass**: jscpd baseline report saved.
- **Stretch**: Copilot Studio bot (HMAC mode).

**Demo**: Open prod URL, sign in, place order, see in admin. Send URL to recruiters.

### Phase 7 — Promotions: Vouchers + Loyalty + Pricing Pipeline (Weeks 17–22)
- Migration `0006_promotions` (Voucher, VoucherRedemption, LoyaltyAccount, LoyaltyTransaction, expand OrderPriceBreakdown).
- **Pricing pipeline**: `IPriceModifier` chain (`SubtotalModifier`, `VoucherModifier`, `LoyaltyRedeemModifier`, `ShippingModifier`, `TaxModifier`) — composable, returns immutable `PriceBreakdown` snapshot.
- **Voucher**: CRUD admin UI (uses `DataTable`, `Modal`); customer apply at cart (`POST /cart/apply-voucher`); strategy-pattern discount types; optimistic concurrency on `UsesRemaining`.
- **Loyalty**: double-entry ledger; `LoyaltyService.EarnAsync` / `RedeemAsync` / `ExpireAsync` / `AdjustAsync`; admin ledger view; customer "My Points" page; redeem at cart.
- **Points-on-ship via Service Bus**: when admin marks order shipped, `OrderShipped` message published; `EarnLoyaltyPointsFn` Function consumes and writes `LoyaltyTransaction` with idempotency key.
- 4 admin reports: voucher usage, loyalty summary, tier distribution, point liability.
- E2E tests for stacking scenarios.

**Demo**: Apply `SAVE10` voucher + redeem 200 pts on cart → checkout → ship → points earn appears in customer's ledger.

### Phase 8 — Event-Driven Architecture (Weeks 23–26)
- Migrate async work from in-process `BackgroundService` to Service Bus + Functions where it makes the resume bullet defensible:
  - `OrderConfirmationFn` (already wired in Phase 2) — fully separate from API
  - `OrderAnomalyScanFn` (was BackgroundService in Phase 5)
  - `ForecastRefreshFn` (was BackgroundService)
  - `PointsExpiryScheduled` / `TierRecalcScheduled` (new)
  - `VoucherRedemptionFn` (decouple voucher counter decrement from checkout hot path)
- Event Grid topic for system events (`OrderPlaced`, `OrderShipped`, `OrderRefunded`, `VoucherRedeemed`, `ReviewPosted`).
- **Baseline vs after** measurement: count synchronous inter-service HTTP calls in Phase 7 baseline vs Phase 8 event-driven path → support 70% reduction claim. Report in `docs/perf/event-driven-coupling.md`.
- Load script generating 10K+ events/day for 24h → App Insights metric screenshot for resume.
- Dead-letter queue handling + runbook (`docs/runbooks/service-bus-dead-letter.md`).

**Demo**: Place order → trace fan-out across Service Bus + Functions in App Insights end-to-end transaction view.

### Phase 9 — Observability + SLA + Runbooks (Weeks 27–29)
- Application Insights workbooks (API health, AI calls, ML jobs, Function execution, Service Bus depth).
- Define SLA per service (e.g. API p95 < 250ms, Function processing < 5s, Stripe webhook ack < 2s).
- Alert rules on SLA violations → email/webhook.
- **Author 10+ runbooks** (`docs/runbooks/`):
  1. API 5xx spike
  2. Stripe webhook signature failures
  3. Service Bus dead-letter queue not empty
  4. SQL serverless throttling
  5. Container Apps cold-start surge
  6. APIM rate-limit blocking legitimate traffic
  7. Anthropic API outage → degraded chat
  8. Forecast Function failure / model drift
  9. Loyalty points award discrepancy investigation
  10. Voucher uses-remaining race condition recovery
- Synthetic alert injection in staging → measure SLA acknowledge rate → report 95%+ within SLA.
- OpenTelemetry custom metrics: `orders.placed`, `vouchers.redeemed`, `loyalty.points.earned`, `ai.copy.generations`, `chat.tool_calls`.

**Demo**: App Insights workbook walk-through; trigger a synthetic alert; show runbook resolution.

### Phase 10 — Perf & Load (Weeks 30–32)
- k6 scripts in `tests/load/`:
  - `catalog-browse.js` — product list with filters/search (B-3 1.1s → 380ms target)
  - `checkout-flow.js` — A-1 500/day + <250ms p95 target
  - `voucher-redemption.js` — concurrency on `UsesRemaining`
  - `event-throughput.js` — A-3 10K events/day
- **Baseline run + optimization pass + post-optim run** for catalog browse:
  1. Baseline measured (likely 800ms–1.5s p95 from realistic data volume seeded in Phase 5).
  2. Optimization: add composite indexes, switch reads to `.AsNoTracking()`, projection to `Select` DTOs, response caching where safe.
  3. Re-measure. Target: ≥55% improvement.
  4. Commit `docs/perf/baseline-{date}.md` + `docs/perf/post-optim-{date}.md` with charts.
- Coverlet coverage check confirms 85%+; jscpd final report confirms ≥45% duplication reduction vs Phase 0 baseline.
- README "Performance & reliability" section linking to all reports.

**Demo**: Side-by-side baseline/optimized k6 charts; explain each optimization with code commits.

## 14. Verification Plan

**Test homes**
- `src/api/Retail.Tests.Unit/` — xUnit, no DB.
- `src/api/Retail.Tests.Integration/` — xUnit + `WebApplicationFactory` + Testcontainers SQL + in-process Service Bus stub (or emulator).
- `src/web/` — Vitest; **Playwright** under `src/web/e2e/` for golden-path: signup → cart → voucher apply → loyalty redeem → Stripe test → see order with full price breakdown.
- `tests/load/` — k6 scripts; results uploaded as `load-test.yml` artifact.

Per-phase manual smoke listed in each phase above. `make verify` script per phase.

## 15. Risks & Mitigations

1. **Year-long timeline → motivation drift / job market timing** — Mitigate by Phase 6 producing a fully demo-able core MVP (already deployed). User can start interviewing with Phase 6 done and continue Phase 7–10 in parallel.
2. **Tailwind hand-built admin slower than Refine scaffolding** — Accepted trade-off for Job B-1 component library claim. Mitigate by getting `components/ui/` mature by end of Phase 1; admin views in Phase 3 then compose, not invent.
3. **Phase 6 Copilot Studio stretch slips** — Phase 5 React widget already proves AI integration.
4. **Azure cost overrun** — $30/mo budget alert; shutdown checklist; `min-replicas=0`; SQL serverless auto-pause.
5. **ML.NET model quality poor on synthetic data** — Seeder injects seasonality; UI tooltip says "synthetic data"; show confidence intervals.
6. **Anthropic API outage during demo** — Feature flag `Ai:Mode=live|stub`; `StubLlmClient` from fixtures.
7. **Stripe webhook replay/drift** — `ProcessedStripeEvent` idempotency; integration test replays 5x; App Insights alert.
8. **Scope creep beyond what bullets demand** — Single rule: if scope doesn't trace to a bullet in `project_resume_targets.md`, push back. AI features carve-out only.
9. **Loyalty points double-award** — `LoyaltyTransaction.IdempotencyKey` unique constraint + Service Bus message dedup.
10. **Voucher uses-remaining race** — `[ConcurrencyCheck]` on `UsesRemaining` + retry-with-reread in `VoucherRedemptionFn`.
11. **APIM cold start latency hits p95** — Use Consumption tier; pre-warm via synthetic ping every 10 min from `load-test.yml`'s lightweight check job.
12. **Event Grid → Function delivery delay during 10K/day test** — Document expected latency in runbook; size Function plan accordingly.

## 16. Confirmed Decisions

| # | Decision | Choice |
|---|---|---|
| 1 | Scope | Realistic small-business MVP + Promotions + Event-driven + Observability + Perf (all 10 phases). Driven by 2 resume role entries. |
| 2 | Timeline | ~31–40 weeks (~7–9 months). Explicitly accepted 2026-06-06. |
| 3 | Chatbot UI | Custom React Tailwind drawer (Phase 5); Copilot Studio Phase 6 stretch |
| 4 | .NET version | .NET 10 LTS (GA Nov 2025; support through Nov 2028) — flipped from .NET 8 on 2026-06-06, see ADR-0006 |
| 5 | Azure region | Australia East (Sydney) |
| 6 | Styling / admin | **React + Tailwind CSS + shadcn/ui (Radix headless)** — flipped from Refine.dev + MUI v5 on 2026-06-06 |
| 7 | Auth | ASP.NET Core Identity + JWT issued as **HTTP-only cookie + refresh-token rotation + CSRF**; 4 roles (Customer / StoreManager / Staff / Administrator) |
| 8 | API gateway | Azure API Management in front of Container Apps |
| 9 | Async work | Azure Service Bus + Event Grid + Azure Functions (plus in-process `BackgroundService` for hot loops) |
| 10 | Payments | Stripe Checkout, test mode only |
| 11 | Promotions | Vouchers + Loyalty (Medium) + unified pricing pipeline, Phase 7 |
| 12 | Component library | 12+ Tailwind primitives, hand-built; jscpd before/after measurement |
| 13 | Perf measurement | k6 baseline + optimization reports committed |
| 14 | Observability | App Insights + Azure Monitor + 10+ runbooks + SLA |
