# Phase 3 — Admin Ops, Audit & 3-Role RBAC: Implementation Scope

> Authoritative pre-build scope for Phase 3 (Epic 3, `PLAN.md:501`). Where this
> doc disagrees with `PLAN.md` / `REQUIREMENTS.md` / `DATABASE_DESIGN.md`, **this
> doc wins for the phase** — the deltas are listed in §3 (decisions) and §4
> (drift reconciliation) rather than silently absorbed; a later docs pass folds
> them back. Companion to `PHASE_2_SCOPE.md`. Source of truth for the phase.

## 1. Goal & demo target

Phase 2 let a customer **buy**. Phase 3 builds the **staff side of those same orders** — the back-office. By the end, the app has a real admin surface with three privilege tiers, an immutable audit trail over every admin mutation, order fulfillment, admin-initiated refunds, basic reporting, and the project's first browser E2E.

**Demo (the acceptance bar, `PLAN.md:509`):** `Administrator`, `StoreManager`, and `Staff` each log in and see a **different sidebar**. Staff fulfills an order (enters carrier + tracking → the order goes `Fulfilled`, a `Shipment` is created). StoreManager refunds an order (Stripe refund → inventory reverses → an audit row is written). Anyone with audit access opens the **AuditLog viewer** and sees those exact mutations (actor, before/after). The **sales-by-day** chart renders. A **Playwright** golden-path run (register → cart → checkout → view order) and an admin run (login → create product → mark shipped) are green in CI with axe a11y assertions.

## 2. Scope boundary

**In:**
- Hand-built **AdminShell** (sidebar + topbar + `Outlet`) and a **role-claim-driven `SidebarNav`**.
- **3-role RBAC** via policy-based authorization (§3.1) across all new admin endpoints + FE route/element guards.
- **Order workbench**: list-all-orders (filter by status / date range / customer email), order detail (payment + shipment), **Mark Shipped**, **Mark Delivered**, **admin refund**.
- **`Shipment`** entity + lifecycle (`Pending`/`Shipped`/`Delivered`); the first code to ever set `OrderStatus.Fulfilled`.
- **`AuditLog`** trail: a dedicated table written by a second SaveChanges interceptor (auto before/after JSON) + explicit named-action rows for refund/ship/inventory-adjust; a searchable viewer.
- **Inventory adjust** (delta + reason → audit).
- **Sales-by-day** report (runtime aggregate + Recharts chart).
- **Thin user-management** (§3.4): seed `Staff`/`StoreManager` demo accounts + a minimal create-account endpoint, so the multi-role demo is reproducible.
- **12+ reusable UI primitives**: build `DataTable`, `Modal`, `Drawer`, `Checkbox`, `Tabs`, `EmptyState`; lift `FilterPanel` into `components/ui`.
- **Vitest** (a few high-value component/hook tests) + **Playwright** golden-path & admin E2E with `@axe-core/playwright`; CI wiring (vitest step, Playwright job, Coverlet collection).

**Out / deferred:**
- **Full user-management** (invite tokens, enable/disable, password reset, email) → later phase (§12.1 marks email as future scope). Phase 3 ships only the thin slice above.
- **Audit / report EXPORT** → deferred. Because export is the *only* thing the matrix lets StoreManager+ do that Staff can't on audit/reports, the "Staff is read-only" nuance is naturally satisfied in MVP (everyone with access only *views*); the `*.Export` policies are defined but unused until an export button exists.
- **Anomaly / risk queue** → Phase 5. The order-workbench `has-anomaly` filter is a wired-but-no-op placeholder in Phase 3.
- **Multi-shipment per order** → future (DATABASE_DESIGN §7 Open Item #2). Phase 3 is strict **1:0..1**.
- **AI copy / voucher / loyalty / app-config** admin pages → their own phases (4/7). The sidebar shows them only as Administrator-gated stubs if at all.
- **`OrderStatusHistory` table** → never (DATABASE_DESIGN §4: AuditLog already captures every status transition).

## 3. Key decisions (2026-06-17)

### 3.1 RBAC = policy-based (hybrid), not role strings on endpoints

`PLAN.md:505` says the matrix is wired "in API (policies)"; `REQUIREMENTS` Task 3.4.1 literally shows `[Authorize(Roles="…")]`. They conflict. **Decision: policy-based**, which satisfies both — `RequireRole(...)` keeps the role *names* in `Roles.cs` as the single source of truth (REQUIREMENTS' intent) while exposing them as named **capability policies** (PLAN's "policies").

**Why** (and why it's best practice here): the matrix is capability-shaped and overlapping — refund = `StoreManager`+`Administrator`, fulfill = `Staff`+`StoreManager`+`Administrator`, manage-catalog = `Administrator`-only. Scattering `Roles="StoreManager,Administrator"` strings across ~6 controllers duplicates each rule in dozens of places; a rename or rule change is a find-replace and a typo silently opens a hole. A named policy is **defined once, applied as `[Authorize(Policy=…)]`**, and changed in one edit. It also separates *what the rule is* from *where it's enforced* and is the natural seam for the later read-only-export nuance and resource-based checks (an order's owner). Cost is ~zero — the JWT already emits `ClaimTypes.Role`, so `RequireRole` policies need no token change.

**The policy set** (defined in one `AddAuthorization` block in `Program.cs`; names live in a `Roles.Policies` static class):

| Policy | Roles | Guards |
|---|---|---|
| `Orders.View` | Staff, StoreManager, Administrator | list-all-orders, order detail |
| `Orders.Fulfill` | Staff, StoreManager, Administrator | mark shipped, mark delivered |
| `Orders.Refund` | StoreManager, Administrator | admin refund |
| `Inventory.Adjust` | Staff, StoreManager, Administrator | stock adjust |
| `Audit.View` | Staff, StoreManager, Administrator | audit search (view-only in MVP) |
| `Reports.View` | Staff, StoreManager, Administrator | sales-by-day |
| `Users.ManageStaff` | StoreManager, Administrator | create/list Staff |
| `Users.ManageManagers` | Administrator | create StoreManager |
| `Catalog.Manage` | Administrator | all catalog writes (migrated off `Roles=Administrator`) |
| `Audit.Export`, `Reports.Export` | StoreManager, Administrator | *defined, unused in MVP (export deferred)* |

The existing storefront `[Authorize(Roles = Roles.Customer)]` attributes **stay as-is** (single-role, not part of the admin matrix). Catalog writes **migrate** from `[Authorize(Roles = Roles.Administrator)]` → `[Authorize(Policy = Policies.CatalogManage)]`.

### 3.2 AuditLog = dual interceptor + named business actions

**Decision:** keep the existing `AuditingInterceptor` (stamps `CreatedBy`/`UpdatedAt` columns — unchanged) and add a **second `AuditTrailInterceptor`** that auto-captures **before/after JSON** rows into the new `AuditLog` table for a monitored allowlist `{Product, InventoryItem, Order, Payment, Shipment}`. The three human-meaningful business actions (**refund**, **ship**, **inventory-adjust**) *also* write an explicit named-action row (`Action = "Refund"` / `"Shipped"` / `"InventoryAdjusted"`) via an injected `IAuditWriter`.

**Why:** REQUIREMENTS §11.1 wants *both* generic CRUD (`Insert`/`Update`/`Delete`) and domain actions; a pure interceptor can't name "Refund" vs a plain `Status` diff, and pure-explicit writes reintroduce the "one missed call = forensic gap" the interceptor pattern exists to prevent. Two interceptors keep each single-responsibility (the existing one's header even anticipates stacking), preserve the current passing tests, and reuse `ICurrentUserAccessor` (`UserId`, null → `"system"`) for `Actor`.

**The mechanism (resume-gold complexity to get right) — as built in Chunk 0:**
- **Single pass in `SavingChanges`/`SavingChangesAsync`.** Every monitored entity has a **client-generated Guid PK** (set when tracked as `Added`, before the SQL runs), so `EntityId` is already known pre-save — no post-save hook needed. The interceptor snapshots the monitored entries into a `List` *first*, builds the `AuditLog` rows (`BeforeJson` from `OriginalValues`, `AfterJson` from `CurrentValues`), and `AddRange`s them to the **same** `ChangeTracker` — so they `INSERT` in the same `SaveChanges`, inside the same transaction as the change they describe (atomic; a rolled-back change rolls back its audit row). `AuditLog` is **not** monitored and we snapshot before adding, so no recursion. The only values not yet materialised are DB-generated `OrderNumber`/`RowVersion`, which we deliberately don't audit.
- **PII:** redaction is by property-name set (`GuestEmail`, `Email`, `Password*`, `Token`, `Secret`, `ShippingAddress`, `BillingAddress`, `RawPayloadJson`) → `"***"`; binary columns (`RowVersion`) are skipped. CODING_STANDARDS flags email/PII as never-log, and `AuditLog` is admin-readable. (Chunk-0 review confirmed the set covers every PII field on the monitored entities.)
- **⚠️ Coverage caveat (Chunk-0 review finding, MEDIUM):** the interceptor only fires on **tracked `SaveChanges`**. **Set-based `ExecuteUpdateAsync` writes bypass it** — the Phase-2 inventory reserve/commit/restock and the `Order` `Paid→Refunding` refund-claim mutate monitored entities with **no** auto audit row. So the Chunk 2/3 admin actions that touch state set-based (**inventory-adjust**, the **refund-claim**, and any `Order`-status flip done via `ExecuteUpdate`) **must emit an explicit `IAuditWriter.Record(...)` row in the same transaction**, or be written as a tracked load+`SaveChanges` so the interceptor captures them. Prefer tracked writes for the new admin order transitions (mark-shipped/deliver) so they're audited for free; use `IAuditWriter` where a set-based write is kept for concurrency. Add a regression test asserting an audit row after each.

### 3.3 Shipment 1:0..1, `Order.Fulfilled` on ship; Shipment carries the fine state

`OrderStatus` has `Fulfilled=3` (today a dead value) and **no** `Shipped`/`Delivered`. **Decision (honors DATABASE_DESIGN §3.14):** `Shipment.Status` carries `Pending`/`Shipped`/`Delivered`; **Mark Shipped** flips `Order.Status` `Paid → Fulfilled` (guarded by `Order.RowVersion`) *and* creates `Shipment(Status=Shipped)`; **Mark Delivered** advances only `Shipment.Status → Delivered`. Do **not** add a `Delivered` `OrderStatus` (no enum renumber; `OrderStatus` is a stored, serialized contract). Cardinality is strict **1:0..1** with `UX_Shipment_OrderId` unique.

### 3.4 User-management = thin demo slice

**Decision:** seed two **dev/Testing-only** demo accounts (`staff@…`, `manager@…`, like the default admin) so the "three roles, three sidebars" demo is reproducible, plus a **minimal create-account endpoint**: `Users.ManageStaff` (SM+Admin) creates `Staff`; `Users.ManageManagers` (Admin) creates `StoreManager`; and a list/filter. **Why:** the demo needs exactly this; the full invite-token/email/enable-disable/reset flow is future scope (§12.1) that Phase 6 may redo. Demo accounts never seed in production.

### 3.5 Sales-by-day = runtime EF `GroupBy`, no view/table

**Decision:** an `IReportQueryService` runs a LINQ `GroupBy` over `Order`/`OrderLine` (by `PlacedAt` date, with a per-category split via `OrderLine → variant → product → category`), converting `int` cents → `decimal` at the projection edge (DATABASE_DESIGN reserves `decimal(18,2)` for "aggregated report views" only). **Why:** portfolio order volume makes a runtime `GROUP BY` trivially fast (`IX_OrderLine_ProductVariantId` already exists), it's testable against Testcontainers SQL Server, and it adds no migration. A materialized view is premature — escalate only if Phase 10 k6 shows a hot path (and that escalation becomes its own perf talking point).

### 3.6 Component library = thin custom `DataTable`, Radix for overlays

**Decision:** build a **thin custom `DataTable`** (sort/page/filter via columns + render props) — *not* `@tanstack/react-table` — to keep the "hand-built reusable library" résumé story honest and avoid a heavy dep. Use Radix only where a11y is hard to hand-roll: `Modal`/`Drawer` (`@radix-ui/react-dialog`), `Checkbox`, `Tabs`. Refactor the existing hand-rolled `AdminProductsPage` table onto `DataTable` to prove reuse (PLAN risk #2 = "compose, not invent").

## 4. Doc-vs-code drifts this phase fixes (recon-verified)

| # | Doc claims | Reality → Phase 3 action |
|---|---|---|
| 1 | DATABASE_DESIGN §5: `Shipment` ships in `0002_orders`, `AuditLog` in `0003_audit` | Both were **dropped/deferred**; `0003` is `customer_profile`. **Migration is `0008_shipment_audit`** (next free; `0000…0007` applied). |
| 2 | `OrderStatus.Fulfilled` is part of the order lifecycle | It exists but is a **dead value** nothing sets. Phase 3's Mark Shipped is the first writer. |
| 3 | `AuditingInterceptor` records the audit trail | It only **stamps columns** — no before/after, no rows. The trail is net-new (a second interceptor). |
| 4 | 3-role RBAC is "wired" | Only `Customer` + `Administrator` are used in `[Authorize]`; `Staff`/`StoreManager` are **seeded names with zero endpoints**, and **no Staff/StoreManager user can exist** (seeder mints one Admin; registration assigns Customer). |
| 5 | PLAN §11: PR gates run `dotnet test --filter Category=Unit/Integration` | **No `Category` traits exist**; the split is by project. Keep the project split; the filter is aspirational (don't add traits just for this). |
| 6 | ci.yml comment promises a Coverlet **85% gate** | Not wired (api job runs bare `dotnet test`). Phase 3 adds **collection now**, flips the failing gate at phase-end (backend-only). |
| 7 | PLAN §14 golden path includes voucher/loyalty | Those are Phase 7. Phase 3 E2E = register→checkout + admin create→ship, structured so voucher/loyalty slot in later. |

## 5. Data model — migration `0008_shipment_audit`

### 5.1 `ShipmentStatus` enum (`Common/Enums/CommerceStatuses.cs`)

`: byte` → `tinyint`, 1-based, default `1`, following the established convention:
```
ShipmentStatus { Pending = 1, Shipped = 2, Delivered = 3 }
```

### 5.2 Entities

**`Shipment`** (`IAuditableEntity`; a child of `Order`):
- `Id` Guid PK; `OrderId` Guid FK **unique** (1:0..1, `Cascade` from Order); `Carrier` nvarchar(60)?; `TrackingNumber` nvarchar(120)?; `Status` `ShipmentStatus` (tinyint, default `Pending`); `ShippedAt` `DateTimeOffset?`; `DeliveredAt` `DateTimeOffset?`; audit columns.
- Indexes: `UX_Shipment_OrderId` (unique), `IX_Shipment_TrackingNumber` (filtered `IS NOT NULL`).
- `Order` gains a `Shipment? Shipment` navigation.

**`AuditLog`** (append-only ledger; **not** `IAuditableEntity`, mirrors `ProcessedStripeEvent`):
- `Id` **bigint identity** PK (narrow clustered key for a high-volume log); `Actor` nvarchar(64) (user id or `"system"`); `Action` nvarchar(40) (`Insert`/`Update`/`Delete` or a business action); `EntityType` nvarchar(120) (CLR type name); `EntityId` nvarchar(64); `BeforeJson` nvarchar(max)?; `AfterJson` nvarchar(max)?; `OccurredAt` `datetime2` default `sysutcdatetime()`.
- Indexes: `IX_AuditLog_OccurredAt`, `IX_AuditLog_EntityType_EntityId`, `IX_AuditLog_Actor_OccurredAt`.

Both get `DbSet`s + `IEntityTypeConfiguration` classes; `RetailDbContext` gains the two sets. `Shipment` joins the `AuditTrailInterceptor` allowlist.

## 6. Authorization design

- One `AddAuthorization` block in `Program.cs` registers the §3.1 policies, each `policy.RequireRole(...)` with names from `Roles.cs`. Policy-name constants live in `Common/Constants/Roles.cs` (`Roles.Policies.*`).
- New admin endpoints use `[Authorize(Policy = Policies.X)]`. Catalog writes migrate to `Policies.CatalogManage`. Storefront `Customer` attributes unchanged.
- **Order-state guards** are not authorization — invalid transitions (e.g. ship an already-`Fulfilled` order, refund a `Cancelled` one) throw `ConflictException` → 409; the ship + refund status transitions are serialized by `Order.RowVersion` → 409 (the Phase-2 pattern). **Refundable state = `Paid` only** → `Refunding` → `Refunded` (revised from the original `{Paid, Fulfilled}` after the Chunk-2 review: refunding a *shipped* order would restock goods already with the customer and strand the shipment — a **return/RMA flow, deferred**). `Refunding` is also accepted as a **recovery** state (a refund that succeeded at Stripe but failed to finish the local reversal is re-drivable, since every step is idempotent).

## 7. Audit trail design

See §3.2. Monitored allowlist `{Product, InventoryItem, Order, Payment, Shipment}`. Generic rows from the interceptor (`Insert`/`Update`/`Delete`); named rows (`Refund`/`Shipped`/`InventoryAdjusted`) from `IAuditWriter` calls inside the three services. `Actor` from `ICurrentUserAccessor`. PII-redacted JSON. The read side (`GET /audit-logs`) is paged + filterable by `actor` / `entityType` / `entityId` / date range.

## 8. Order workbench, fulfillment & refund

- **Admin query** (`IAdminOrderService` / `AdminOrderRepository`): all-orders paged, filters `status` / date range / `customerEmail` / `hasAnomaly` (no-op placeholder), ordered by `PlacedAt` desc (backed by `IX_Order_Status_PlacedAt`). Detail includes payments + shipment.
- **Mark Shipped** (`Orders.Fulfill`): create `Shipment(Carrier, TrackingNumber, Status=Shipped, ShippedAt)`, flip `Order.Status Paid→Fulfilled` under `RowVersion`, write a `Shipped` audit row — one transaction.
- **Mark Delivered** (`Orders.Fulfill`): `Shipment.Status → Delivered`, `DeliveredAt`.
- **Admin Refund** (`Orders.Refund`): reuse the Phase-2 refund machinery — a `Paid`-only admin claim (`TryClaimForRefundByIdAsync`, `Paid → Refunding`), `StripeRefundGateway` with the deterministic `refund:{pi}` idempotency key, then the idempotent `OrderRefundService` reversal — initiated by an authenticated actor, plus a named `Refund` audit row **staged before the reversal so it commits in the same transaction**. The whole path is re-drivable from `Refunding` (recovery). Reuses, not duplicates, the existing reversal. *(Shipped-order refunds = a return/RMA flow, deferred to a later phase.)*

## 9. Reporting — sales-by-day

`IReportQueryService.GetSalesByDayAsync(from, to)` → EF `GroupBy` on `PlacedAt::date` over orders in revenue states, returning per-day `{ date, orderCount, totalSales (decimal), categorySplit[] }`. New `AnalyticsController` `GET /api/v1/analytics/sales-by-day` (`Reports.View`). FE: a Recharts line chart + `EmptyState` (data is sparse until the Phase-5 synthetic-order seeder — documented in the demo script, no throwaway Phase-3 seeder).

## 10. User management (thin)

- **Seeder:** dev/Testing-only `staff@…` + `manager@…` demo accounts (skipped in production, like the default admin).
- **API** (`AdminUsersController`): `GET /api/v1/admin/users` (list, filter by role; `Users.ManageStaff`), `POST /api/v1/admin/users` (create — `Staff` requires `Users.ManageStaff`, `StoreManager` requires `Users.ManageManagers`). Enable/disable, reset, invite-tokens → deferred.

## 11. API surface (new)

```
AdminOrdersController      GET  /api/v1/admin/orders                 (Orders.View)   paged + filters
                          GET  /api/v1/admin/orders/{id}            (Orders.View)   detail + payment + shipment
                          POST /api/v1/admin/orders/{id}/ship       (Orders.Fulfill)
                          POST /api/v1/admin/orders/{id}/deliver    (Orders.Fulfill)
                          POST /api/v1/admin/orders/{id}/refund     (Orders.Refund)
AdminInventoryController  POST /api/v1/admin/inventory/{variantId}/adjust  (Inventory.Adjust)  {delta, reason}
AuditLogsController       GET  /api/v1/audit-logs                   (Audit.View)    search + paged
AnalyticsController       GET  /api/v1/analytics/sales-by-day       (Reports.View)  ?from=&to=
AdminUsersController      GET  /api/v1/admin/users                  (Users.ManageStaff)
                          POST /api/v1/admin/users                  (Users.ManageStaff | Users.ManageManagers)
CatalogController         (existing writes) Roles=Administrator  →  Policy=Catalog.Manage
```
All return the standard `ApiResponse<T>` envelope; lists ride `PagedResult<T>`; query DTOs follow the `[FromQuery]` PascalCase convention.

## 12. Frontend surface

- **`AdminShell`** (sidebar + topbar + `Outlet`); all `/admin/*` routes nest under it. **`SidebarNav`** renders items from a single **capability map** (`ROLE_SETS`) mirroring the BE policy names — consumed by both the nav and the `RoleGuard` `allowedRoles`, so route gating, menu, and per-element gating (hide the Refund button from Staff) can't drift.
- **Primitives (→ 12+):** `DataTable` (thin custom), `Modal` + `Drawer` (Radix Dialog), `Checkbox`, `Tabs` (Radix), `EmptyState`; lift `FilterPanel` into `components/ui`. Refactor `AdminProductsPage` onto `DataTable`.
- **Pages:** order workbench (list + detail), Mark-Shipped / Mark-Delivered / Refund modals, AuditLog viewer, sales-by-day chart, users page.
- New deps: `@radix-ui/react-dialog`, `@radix-ui/react-checkbox`, `@radix-ui/react-tabs`, `recharts`.

## 13. Testing & E2E plan

- **Backend RBAC matrix:** a test helper minting `Staff`/`StoreManager`/`Administrator` cookies (ApiFactory seeds them or a create+login helper); the Task 3.4.3 **forged-Customer-JWT → 403** test plus a 4-role × endpoint allow/deny matrix.
- **Audit integration:** ship/refund/inventory-adjust each write a correct `AuditLog` row (actor, action, before/after).
- **Fulfillment/refund integration:** Mark Shipped sets `Fulfilled` + creates `Shipment`; admin refund reverses inventory + writes the negative `Payment` + audit; `RowVersion` concurrency on the transitions.
- **Reporting integration:** seed orders across days → assert the per-day/per-category buckets.
- **Vitest** (greenfield: `vitest` + `@testing-library/react` + jsdom): ~3–5 high-value tests (`RoleGuard`, `SidebarNav` role filtering, `DataTable`, a report hook). Resist broad FE coverage.
- **Playwright** (`@playwright/test` + `@axe-core/playwright`, `src/web/e2e/`): golden path (register → cart → checkout → view order) **intercepting the Stripe hosted redirect** (route-mock / reuse the fake-gateway path; Stripe's page can't be driven in headless CI) and resuming at the confirmation page; admin flow (login → create product → mark shipped); axe a11y asserts.
- **CI:** add `pnpm vitest run` to the web job; add a **Playwright job** (stack via docker-compose + `vite preview`, managed Chromium, Stripe intercepted, **per-PR** not nightly); **Coverlet** `--collect` now, flip the **85%** failing gate at phase-end (backend-only). Swagger↔TS drift check optional.

## 14. Chunking (each independently buildable + verifiable)

- **Chunk 0 — Data model & cross-cutting foundation.** `ShipmentStatus` enum; `Shipment` + `AuditLog` entities + configs + DbSets; migration `0008_shipment_audit`; the `AuditTrailInterceptor` + `IAuditWriter`; the `AddAuthorization` policy block + `Roles.Policies`. *Verify:* build 0/0, migration applies, audit rows appear for a plain product edit, policies resolve.
- **Chunk 1 — RBAC + AdminShell + thin users.** Migrate catalog writes to `Catalog.Manage`; seed `Staff`/`StoreManager`; `AdminUsersController` create/list; FE `AdminShell` + `SidebarNav` + capability map + re-scoped guards; `DataTable` + `Modal` primitives; refactor `AdminProductsPage` onto `DataTable`. *Verify:* the three-roles-three-sidebars demo; forged-Customer-JWT → 403; role-matrix test helper.
- **Chunk 2 — Order workbench + fulfillment + refund.** Admin order query/detail; ship/deliver/refund endpoints (RowVersion-guarded + audit rows); FE workbench (`DataTable`) + Mark-Shipped/Deliver/Refund modals. *Verify:* ship sets `Fulfilled` + `Shipment`; admin refund reverses + audits; concurrency 409s.
- **Chunk 3 — Audit viewer + reporting + inventory adjust.** `GET /audit-logs` search + FE viewer; sales-by-day endpoint (LINQ `GroupBy`) + Recharts page + `EmptyState`; inventory-adjust endpoint + audit. *Verify:* audit search returns the Chunk-2 rows; report buckets; adjust writes audit.
- **Chunk 4 — E2E + CI gates.** Vitest setup + the ~3–5 component tests; Playwright golden-path + admin flow + axe; CI wiring (vitest step, Playwright job, Coverlet collection → flip 85% gate). *Verify:* both E2E specs green in CI; gate enforced.

## 15. Resume-bullet alignment

- **Job A (backend/platform):** policy-based **RBAC** with a forged-JWT-→403 matrix (access-control bullet); the **AuditLog interceptor** capturing before/after JSON across Product/Inventory/Order/Payment/Shipment (observability/forensics bullet); **admin Stripe refund** that atomically reverses inventory + audits under optimistic concurrency (payments/transactional-integrity bullet); the wired **Coverlet 85% gate** + Testcontainers integration matrix (testing/CI bullet).
- **Job B (frontend/product):** the **12+ hand-built primitives** (`DataTable`/`Modal`/`Drawer`/`Checkbox`/`Tabs`/`EmptyState`) — "compose, not invent" (component-library bullet); the **role-driven AdminShell** showing different nav per role (the literal "store managers, staff, administrators" bullet); **Vitest + Playwright + axe** E2E (accessible, tested UI / deployment-success bullet).

Every Phase-3 item maps to a matrix permission or a named bullet (PLAN risk #8) — which is why user-management stays a thin slice and the sales report grows no throwaway seeder.

## 16. Open items / follow-ups

- Full user-management (invite tokens, enable/disable, reset, email) — deferred to a later phase.
- Audit/report **export** — deferred; the `*.Export` policies are defined but unused until an export button exists (which is what makes the "Staff read-only" nuance real).
- Anomaly/risk-queue filter — Phase 5; the workbench filter is a no-op placeholder now.
- Multi-shipment per order — future; the clean change is dropping `UX_Shipment_OrderId`.
- **PII in AuditLog JSON** — ✅ done in Chunk 0 (property-name redaction set; review-confirmed it covers every PII field on the monitored entities).
- **Audit coverage of set-based writes (Chunk-0 review, MEDIUM)** — `ExecuteUpdateAsync` bypasses the interceptor, so the Chunk 2/3 inventory-adjust, refund-claim, and any `Order`-status flip done set-based must emit an explicit `IAuditWriter` row (or use a tracked write). Tracked by §3.2; enforce with a per-action regression test in Chunk 2/3.
- Coverlet **85% gate** flips at phase-end (collect from Chunk 0) to avoid blocking WIP PRs.
- A one-line correction to `DATABASE_DESIGN.md` migration-history (Shipment/AuditLog are `0008`, not `0002`/`0003`) at the next docs pass.
