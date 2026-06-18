# Phase 3 Recap — What You Built and Why

> A self-learning recap of every concept, file, and connection introduced in
> Phase 3 (Epic 3 — Admin Ops, Audit, 3-role RBAC, built as Chunks 0–4 plus the
> adversarial review + fixes pass). Read top to bottom the first time; later, use
> the table of contents to jump back to specific patterns. Companion to
> `phase0_recap.md` (the seams), `phase1_recap.md` (catalog + account), and
> `phase2_recap.md` (cart + orders). Phase 2 let a shopper **buy**; **Phase 3
> gives the business the back-office to fulfil, refund, and audit what they
> bought** — behind a three-role policy gate.

## Table of contents

1. [The big picture](#1-the-big-picture)
2. [Chunk 0 — The data model, the audit interceptor, the RBAC policy block](#2-chunk-0--the-data-model-the-audit-interceptor-the-rbac-policy-block)
3. [Chunk 1 — RBAC enforcement, the AdminShell, thin user management](#3-chunk-1--rbac-enforcement-the-adminshell-thin-user-management)
4. [Chunk 2 — The order workbench (read + fulfilment + admin refund)](#4-chunk-2--the-order-workbench-read--fulfilment--admin-refund)
5. [Chunk 3 — Audit viewer, sales report, inventory adjust](#5-chunk-3--audit-viewer-sales-report-inventory-adjust)
6. [The frontend — AdminShell, the capability mirror, the admin pages](#6-the-frontend--adminshell-the-capability-mirror-the-admin-pages)
7. [Chunk 4 — The testing surface + the CI gates](#7-chunk-4--the-testing-surface--the-ci-gates)
8. [The review + fixes pass](#8-the-review--fixes-pass)
9. [File relationship maps](#9-file-relationship-maps)
10. [Patterns to remember (interview material)](#10-patterns-to-remember-interview-material)
11. [What's next — Phase 4 preview](#11-whats-next--phase-4-preview)

---

## 1. The big picture

### What Phase 3 turned on

Phase 2 left you with a store that **transacts**: a shopper fills a cart, pays through Stripe, and an idempotent webhook mints an `Order`. But every actor so far has been a *customer*. There was no one on the other side of the counter. The four roles in `Roles.cs` were, as `PHASE_3_SCOPE.md §4` (drift #4) admits, "**seeded names with zero endpoints**" — `Staff` and `StoreManager` couldn't even *exist* as users (the seeder mints one Admin; registration assigns Customer). Phase 3 builds **the staff side of those same orders** — a real back-office.

By the end, the business has a policy-gated admin surface with **three privilege tiers**. `Staff`, `StoreManager`, and `Administrator` each log in and see a *different sidebar*. Staff fulfils an order (enter carrier + tracking → `Order.Status` flips `Paid → Fulfilled`, a `Shipment` row is born). StoreManager refunds an order (Stripe refund → inventory reverses → an audit row lands). Anyone with audit access opens the **AuditLog viewer** and sees those exact mutations — *actor, before, after*. A **sales-by-day** chart renders. And the project gets its first **Playwright** browser E2E (golden path + admin flow, with axe a11y assertions) green in CI.

The throughline is forensics and authority: where Phase 2 was about *money moving correctly*, Phase 3 is about *who is allowed to touch it* and *proving what they did*.

It was built as five chunks, each independently buildable and verifiable:

| Chunk | What shipped |
|---|---|
| **0 Foundation** | The cross-cutting plumbing the rest stands on: the `ShipmentStatus` enum (5th byte-enum → `tinyint`); the `Shipment` + `AuditLog` entities + configs + DbSets; migration `0008_shipment_audit`; the **second SaveChanges interceptor** (`AuditTrailInterceptor`) + the `IAuditWriter` seam; and the **one `AddAuthorization` policy block** + `Roles.Policies`. |
| **1 RBAC + AdminShell + thin users** | Catalog writes migrate `Roles=Administrator → Policy=Catalog.Manage`; dev-only `staff@`/`manager@` seed accounts; `AdminUsersController` create/list; the hand-built **`AdminShell`** + **`SidebarNav`** driven off a single capability map (`ROLE_SETS`); the `DataTable` + `Modal` primitives; `AdminProductsPage` refactored onto `DataTable` to prove reuse. |
| **2 Workbench + fulfillment + refund** | The admin order query/detail (`IAdminOrderService`); **Mark Shipped** / **Mark Delivered** / **admin Refund** endpoints — `RowVersion`-guarded transitions that emit audit rows; the FE workbench + modals. The first code ever to set `OrderStatus.Fulfilled`. |
| **3 Audit viewer + reporting + inventory** | `GET /audit-logs` paged search + viewer; the **sales-by-day** endpoint (runtime LINQ `GroupBy`, no view/table) + Recharts page + `EmptyState`; the inventory-adjust endpoint (`{delta, reason}` → audit). |
| **4 E2E + CI gates** | Vitest setup + ~3–5 high-value component/hook tests; **Playwright** golden-path + admin specs with `@axe-core/playwright`; CI wiring (vitest step, Playwright job, Coverlet `--collect` → the 85% gate flips backend-only at phase-end). |

### The Phase 2 seam → Phase 3 use

Phase 3 is unusually *seam-heavy*: almost nothing here is greenfield invention — it's reuse of machinery Phase 2 left in place, which is the whole point of the architecture paying off.

| Phase 2 seam | Phase 3 use |
|---|---|
| `AuditingInterceptor` (stamps `CreatedBy`/`UpdatedAt`) | **Joined, not replaced**, by a second `AuditTrailInterceptor` — EF composes both on `AddInterceptors`. The existing one's header even *anticipated* stacking. |
| `InventoryItem.RowVersion` (Phase-2 reserve guard) | **Reused** as the optimistic-concurrency token for the admin **stock adjust** (`{delta, reason}`). |
| `Order.RowVersion` (Phase-2 cancel-vs-refund guard) | **Reused** to serialize the **ship** and **refund** status transitions — a concurrent ship → 409, the Phase-2 pattern. |
| `OrderRefundService` (Phase-2 customer-cancel reversal) | **Reused, not duplicated**, by the admin refund — same idempotent inventory-reversal + negative-`Payment` machinery, initiated by an authenticated actor. |
| `[Authorize(Roles = Roles.Customer)]` storefront attrs | **Stay as-is** (single-role). The *admin* surface moves to **policy-based**; catalog writes migrate off `Roles=Administrator`. |
| `openapi-fetch` typed client + `Roles.cs` names | The FE `ROLE_SETS` capability map **mirrors** the BE `Roles.Policies` names in one place — nav, route guards, and per-element gating can't drift. |
| The Phase-1/2 hand-built primitive library | `DataTable`, `Modal`, `EmptyState` **join** it (→ 13), keeping the "compose, not invent" résumé story honest. |

### The vertical slice — the shape the whole phase takes

```
React (admin): SidebarNav + RoleGuard read ROLE_SETS (mirror of Roles.Policies)
   │  apiClient (typed, CSRF header, httpOnly JWT cookie carrying ClaimTypes.Role)
   ▼
[Authorize(Policy = Roles.Policies.X)]   ← AddAuthorization block: each policy = RequireRole(...)
   Controller                              over the canonical Roles.* names. Forged-Customer JWT → 403.
   │
   ▼
AdminOrderService / AdminInventoryService / AuditQueryService / ReportQueryService
   │  business rules + the multi-table TRANSACTION; throws ConflictException (bad transition) → 409
   │
   ├─ named row →  IAuditWriter.Record("Refund"/"Shipped"/"InventoryAdjusted", …)  ──┐
   ▼                                                                                  │  (same DbContext,
RetailDbContext.SaveChanges                                                          │   same SaveChanges,
   │   ▼ AuditingInterceptor       stamps CreatedBy/UpdatedAt on the changed row     │   same transaction)
   │   ▼ AuditTrailInterceptor      appends AuditLog before/after JSON for           │
   │                                {Product, InventoryItem, Order, Payment, Shipment} ◄─┘
   ▼
SQL Server  ← Shipment (1:0..1, UX_Shipment_OrderId); AuditLog (bigint identity, append-only)
```

Two interceptors fire in one `SaveChanges`, the named `IAuditWriter` row rides the *same* transaction as the business change it describes, and the whole thing is gated by a policy resolved from a role claim the JWT already carried. That single picture is the phase.

### The design bets

Three load-bearing decisions (`PHASE_3_SCOPE.md §3`), each made explicit so they don't read as accidents:

1. **Policy-based RBAC over scattered role strings.** `PLAN.md:505` says the matrix is wired "in API (policies)"; `REQUIREMENTS` Task 3.4.1 literally shows `[Authorize(Roles="…")]`. They conflict. The resolution satisfies both: each capability is a **named policy** — `RequireRole(...)` over names from `Roles.cs` (REQUIREMENTS' intent) exposed as a policy key (PLAN's "policies"). The matrix is capability-shaped and *overlapping* (refund = SM+Admin, fulfil = Staff+SM+Admin, catalog = Admin-only); scattering `Roles="StoreManager,Administrator"` across ~6 controllers duplicates each rule in dozens of places where one typo silently opens a hole. A named policy is **defined once, applied as `[Authorize(Policy=…)]`, changed in one edit**. Cost is ~zero — the JWT already emits `ClaimTypes.Role`.

2. **Dual interceptor + named business rows.** Keep `AuditingInterceptor` (stamps columns) untouched; add `AuditTrailInterceptor` for auto before/after JSON over the monitored allowlist; *and* let the three human-meaningful actions write an explicit `IAuditWriter` row. A pure interceptor can't name "Refund" vs a plain `Status` diff; pure-explicit writes reintroduce the "one missed call = forensic gap" the interceptor exists to prevent. Two interceptors keep each single-responsibility and preserve every passing test.

3. **Thin user-management.** Seed dev-only `staff@`/`manager@` demo accounts (skipped in production, like the default admin) + a *minimal* create endpoint — exactly enough to make "three roles, three sidebars" reproducible. The full invite-token / email / enable-disable / reset flow is deferred future scope; building it now would be speculative.

### Conventions locked this phase

- **Named capability policies live in `Roles.Policies`** — the single source of truth for the matrix. Controllers reference a key (`Roles.Policies.OrdersRefund`), never an inline role string. The FE `ROLE_SETS` map mirrors them.
- **`ShipmentStatus` is the continuation of the byte-enum → `tinyint` convention** — `: byte`, 1-based, default `1`:

  ```csharp
  public enum ShipmentStatus : byte
  {
      Pending = 1,
      Shipped = 2,
      // Delivered = 3
  }
  ```
  Critically, `ShipmentStatus` carries the fine-grained `Pending`/`Shipped`/`Delivered` state so **`OrderStatus` is never renumbered** — Mark Shipped flips `Order.Status Paid → Fulfilled` (a previously dead value) and Mark Delivered advances *only* `Shipment.Status`. `OrderStatus` is a stored, serialized contract; adding a `Delivered` member would re-map existing rows.
- **Set-based `ExecuteUpdate` writes MUST emit an `IAuditWriter` row.** The interceptor only fires on **tracked `SaveChanges`** — `ExecuteUpdateAsync` bypasses it entirely. So any admin action that mutates a monitored entity set-based (the inventory adjust, the refund-claim flip) must stage an explicit `IAuditWriter.Record(...)` *in the same transaction*, or be rewritten as a tracked load+save. This was the Chunk-0 review's MEDIUM finding (`§16`), and the closure is enforced with a per-action regression test.

The policy wiring sits in one block in `Program.cs`, exactly three role-set arrays feeding eleven `AddPolicy` calls:

```csharp
string[] staffPlus = { Roles.Staff, Roles.StoreManager, Roles.Administrator };
string[] managerPlus = { Roles.StoreManager, Roles.Administrator };
string[] adminOnly = { Roles.Administrator };

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Roles.Policies.OrdersView, p => p.RequireRole(staffPlus));
    options.AddPolicy(Roles.Policies.OrdersRefund, p => p.RequireRole(managerPlus));
    options.AddPolicy(Roles.Policies.CatalogManage, p => p.RequireRole(adminOnly));
    // …
});
```

And `IAuditWriter`'s contract makes the atomicity promise explicit — the row is added to the *current* `RetailDbContext`, so it commits with the business change:

```csharp
void Record(string action, string entityType, string entityId,
            object? before = null, object? after = null);
```

### Why these choices matter for the resume

| Resume claim | The Phase 3 evidence |
|---|---|
| "Policy-based RBAC across a 3-tier admin matrix; forged-Customer-JWT → 403" | The one `AddAuthorization` block (11 `RequireRole` policies over `Roles.*`), `[Authorize(Policy = Roles.Policies.X)]` on `AdminOrders`/`AdminInventory`/`AuditLogs`/`Analytics`/`AdminUsers`/`Catalog` controllers, the forged-JWT integration test |
| "Immutable audit trail capturing before/after JSON across 5 entity types" | `AuditTrailInterceptor` (second SaveChanges interceptor, one-pass capture-and-add, monitored `{Product, InventoryItem, Order, Payment, Shipment}`, PII-redaction set) + `IAuditWriter` named rows |
| "Admin Stripe refund that atomically reverses inventory + audits under optimistic concurrency" | Reuse of `OrderRefundService` + `Order.RowVersion` guard, the `Refund` audit row staged in the same transaction, the `Refunding` recovery state |
| "Hand-built reusable component library (compose, not invent)" | `DataTable` (thin custom, *not* `@tanstack/react-table`), `Modal` (Radix Dialog), `EmptyState` → 13 primitives; `AdminProductsPage` refactored onto `DataTable` |
| "Role-driven admin shell — store managers, staff, administrators each see their own nav" | `AdminShell` + `SidebarNav` reading the single `ROLE_SETS` capability map that mirrors `Roles.Policies` |
| "Browser E2E with accessibility assertions in CI" | Playwright golden-path + admin specs + `@axe-core/playwright`; Coverlet 85% gate flipped backend-only at phase-end |
| "Runtime aggregate reporting without premature materialization" | `IReportQueryService` LINQ `GroupBy` over `Order`/`OrderLine` (int cents → decimal at the projection edge), Testcontainers-tested, zero migration |

---

## 2. Chunk 0 — The data model, the audit interceptor, the RBAC policy block

This is the schema-and-plumbing foundation Phase 3 stands on. Where Phase 2's Chunk 0 introduced the *commerce* enums and aggregates, Phase 3's Chunk 0 adds the two things admin operations need before any admin endpoint can exist: a **place to record fulfilment** (the `Shipment` entity + `ShipmentStatus`), an **immutable record of who-did-what** (the `AuditLog` entity, written two ways — by a *second* `SaveChanges` interceptor and by an explicit `IAuditWriter`), and the **policy block** that turns the four role names into 11 named capability policies. Migration `0008_shipment_audit` materializes the two new tables. The interview meat here is the audit subsystem: an append-only ledger that inserts *in the same transaction* as the change it describes, and the deliberate choice to keep it out of `IAuditableEntity`.

### What is in Chunk 0

```
src/api/Retail.Api/
├─ Common/Enums/CommerceStatuses.cs          ← +ShipmentStatus (5th byte-enum: Pending→Shipped→Delivered)
├─ Common/Constants/Roles.cs                 ← +Roles.Policies (11 named policy keys = the capability matrix)
├─ Domain/Entities/
│  ├─ Shipment.cs                            ← fulfilment record, 1:0..1 to Order, IAuditableEntity
│  ├─ AuditLog.cs                            ← bigint-identity append-only ledger, NOT IAuditableEntity
│  └─ Order.cs                               ← gains the Shipment? nav (Fulfilled-on-ship; no new OrderStatus)
├─ Common/Abstractions/
│  ├─ IAuditWriter.cs / AuditWriter.cs       ← explicit named-business-action audit rows (no SaveChanges)
├─ Data/Interceptors/AuditTrailInterceptor.cs ← the SECOND SaveChanges interceptor (auto before/after JSON)
├─ Data/Configurations/
│  ├─ ShipmentConfiguration.cs               ← UX_Shipment_OrderId + filtered tracking index
│  └─ AuditLogConfiguration.cs               ← bigint identity + the 3 search indexes
├─ Data/RetailDbContext.cs                   ← +Shipments, +AuditLogs DbSets
├─ Data/Migrations/20260617095311_0008_shipment_audit.cs  ← AuditLog + Shipment tables
└─ Program.cs                                ← AddInterceptors(both) + the AddAuthorization policy block
```

### Per-file purpose

#### `Common/Enums/CommerceStatuses.cs` — the new `ShipmentStatus` (resume-gold)

Phase 3 adds a **fifth** byte-enum to the same file, following the locked convention exactly — `: byte` → `tinyint`, 1-based, never renumbered, default `1`:

```csharp
public enum ShipmentStatus : byte
{
    Pending = 1,   // Shipment row exists but nothing has been dispatched yet.
    Shipped = 2,   // Dispatched with a carrier — ShippedAt stamped, order is Fulfilled.
    Delivered = 3, // Confirmed delivered — DeliveredAt stamped.
}
```

The load-bearing design decision is in the doc comment: the **finer logistics progression lives on the `Shipment`, not on `OrderStatus`.** "Mark Shipped" flips the order to `OrderStatus.Fulfilled` *once*, and the order-level status then stays `Fulfilled` — the `Pending → Shipped → Delivered` granularity is tracked here on the shipment. The reason is the same contract-stability reason `Refunding` was *appended* as `6` in Phase 2: `OrderStatus` is a **stored, serialized contract**. Adding `Shipped`/`Delivered` to it would either renumber existing members (silently re-mapping every stored row) or bloat the order lifecycle with logistics states that don't belong to the order aggregate. Splitting the two lets fulfilment progress without ever touching the `OrderStatus` numbering. **Interview framing:** "where does a status live?" is answered by *which aggregate owns the lifecycle* — the order owns "is this fulfilled?", the shipment owns "where is the parcel?".

#### `Domain/Entities/Shipment.cs`

The fulfilment record — deferred from Phase 2's Chunk 0 (it was modeled in `DATABASE_DESIGN §3.14` but not built until staff ops needed it). It **is** an `IAuditableEntity` (it's a real domain row staff mutate, so it earns `CreatedBy`/`UpdatedAt` stamping), and it carries its own status plus two nullable timestamps:

```csharp
public Guid OrderId { get; set; }              // FK — UNIQUE (one shipment per order, 1:0..1)
public Order Order { get; set; } = null!;
public string? Carrier { get; set; }           // null until shipped
public string? TrackingNumber { get; set; }    // null until shipped
public ShipmentStatus Status { get; set; } = ShipmentStatus.Pending;
public DateTimeOffset? ShippedAt { get; set; }   // set on "Mark Shipped"
public DateTimeOffset? DeliveredAt { get; set; } // set on "Mark Delivered"
```

Two cardinality decisions:

- **1:0..1 to `Order`.** An order has *at most one* shipment in the MVP, enforced by `UX_Shipment_OrderId` (below). The XML doc is explicit that multi-shipment is a future extension — and the migration path is clean: *drop the unique index*. Modeling it as `0..1` (the `Shipment?` nav on `Order` is nullable) rather than creating an empty shipment at order placement means an unfulfilled order simply has *no* shipment row, which is the truthful representation and keeps the table empty until staff actually ship something.
- **`Carrier`/`TrackingNumber` nullable.** A `Pending` shipment can exist (e.g. created at pick time) before a carrier is assigned, so both are null-until-shipped rather than `NOT NULL`.

#### `Domain/Entities/Order.cs` — the one new line

`Order` gains exactly one member this phase — the inverse nav for the 1:0..1:

```csharp
/// <summary>The 1:0..1 fulfilment shipment — null until staff "Mark Shipped" (Phase 3).</summary>
public Shipment? Shipment { get; set; }
```

Notably, **`OrderStatus` is unchanged** — no new status value was added for shipping. That's the `ShipmentStatus`-split decision paying off: the order's existing `Fulfilled = 3` (which Phase 2 reserved but never set) is what "Mark Shipped" finally writes, and the nullable nav is the only structural change to the aggregate root.

#### `Domain/Entities/AuditLog.cs` (resume-gold)

The immutable "who did what, when, and what changed" ledger. The interview-critical part is what it **deliberately is not**:

```csharp
public class AuditLog   // ← NOT : IAuditableEntity
{
    public long Id { get; set; }              // bigint IDENTITY — narrow monotonic clustered key
    public string Actor { get; set; }          // Identity user id, or "system"
    public string Action { get; set; }         // "Insert"/"Update"/"Delete" OR "Refund"/"Shipped"/...
    public string EntityType { get; set; }     // CLR type name, e.g. "Order"
    public string EntityId { get; set; }       // affected PK as a string
    public string? BeforeJson { get; set; }    // PII-redacted; null on inserts
    public string? AfterJson { get; set; }     // PII-redacted; null on deletes
    public DateTimeOffset OccurredAt { get; set; }
}
```

Three decisions, each defensible cold:

- **`long` `bigint IDENTITY` PK, not a `Guid`.** This is the exact reasoning `ProcessedStripeEvent` used in Phase 2: an ever-growing append-only log wants a **narrow, monotonic clustered key** so inserts always land at the end of the index. A random-GUID clustered key would scatter inserts across pages and fragment the table — the cost compounds as the log grows, which for an audit log is *forever*.
- **NOT `IAuditableEntity`.** A row that records "who changed things" has no sensible "who *updated this audit row*" story — audit rows are write-once. Implementing the interface would add four meaningless columns and, worse, invite the auditing machinery to touch it.
- **`Action` is a union of two vocabularies.** It holds either a generic CRUD verb (`Insert`/`Update`/`Delete`, written by the interceptor) *or* a named business action (`Refund`/`Shipped`/`InventoryAdjusted`, written by `IAuditWriter`). That dual nature is the whole "written two ways" design: the auto-capture gives you the *data diff* for free; the named row gives you the *human intent* a status-diff can't express.

`OccurredAt` is set from the **injected clock, not a DB default** (`GETUTCDATE()`), so tests are deterministic — the same `TimeProvider` discipline as every other timestamp in the project.

#### `Data/Configurations/AuditLogConfiguration.cs`

Maps the bigint identity and — the part that makes the log *usable* — the **three search indexes** matching the three axes the viewer exposes:

```csharp
builder.Property(a => a.Id).ValueGeneratedOnAdd();   // bigint IDENTITY
// ...
builder.HasIndex(a => a.OccurredAt, "IX_AuditLog_OccurredAt");                                   // recent-first
builder.HasIndex(a => new { a.EntityType, a.EntityId }, "IX_AuditLog_EntityType_EntityId");      // "what happened to THIS order"
builder.HasIndex(a => new { a.Actor, a.OccurredAt }, "IX_AuditLog_Actor_OccurredAt");            // "what did THIS user do over time"
```

The composite `(Actor, OccurredAt)` is ordered actor-first deliberately — it serves "everything this actor did, newest first" as a single index seek + range scan, which a `(OccurredAt, Actor)` index couldn't. The `BeforeJson`/`AfterJson` columns are `nvarchar(max)` (unbounded diff snapshots), while the descriptor columns are tightly capped (`Actor` 64, `Action` 40, `EntityType` 120, `EntityId` 64) so the indexed columns stay narrow.

#### `Data/Configurations/ShipmentConfiguration.cs`

Two indexes carry the cardinality and the lookup story:

```csharp
builder.HasOne(s => s.Order)
    .WithOne(o => o.Shipment)
    .HasForeignKey<Shipment>(s => s.OrderId)
    .OnDelete(DeleteBehavior.Cascade);

builder.HasIndex(s => s.OrderId, "UX_Shipment_OrderId").IsUnique();

builder.HasIndex(s => s.TrackingNumber, "IX_Shipment_TrackingNumber")
    .HasFilter("[TrackingNumber] IS NOT NULL");
```

- **`UX_Shipment_OrderId` (unique)** is what *enforces* the 1:0..1 — without it, two "Mark Shipped" calls racing the same order could create two shipment rows. And as the comment notes, dropping exactly this index is the clean migration to multi-shipment, so the unique constraint is doing double duty as a documented seam.
- **The filtered tracking index** (`WHERE [TrackingNumber] IS NOT NULL`) supports "find by tracking number" without indexing the many `Pending` rows whose tracking is still null — the same filtered-index pattern Phase 2 used for `UX_Payment_StripeSessionId`. It's deliberately **not unique** (carriers can reuse tracking numbers across time/regions).
- **`Cascade` on the FK** is safe here because a shipment is unambiguously a *child* of one order (single parent — no multiple-cascade-path problem like `InventoryReservation` had), and orders aren't hard-deleted in practice, so it rarely fires.

#### `Data/Interceptors/AuditTrailInterceptor.cs` (resume-gold)

The headline file of the chunk: the **second** `SaveChanges` interceptor, sitting alongside Phase 0's `AuditingInterceptor`. The distinction is the whole design — and a classic interview question ("why two interceptors?"):

| | `AuditingInterceptor` (Phase 0) | `AuditTrailInterceptor` (Phase 3) |
|---|---|---|
| Job | **STAMPS** `CreatedBy`/`UpdatedAt` columns on the changing row | **APPENDS** an immutable `AuditLog` row (actor + before/after JSON) |
| Effect | mutates the *same* row | inserts a *different* row in a *different* table |
| Scope | every `IAuditableEntity` | only the `Monitored` allowlist |

Keeping them separate leaves each single-responsibility and the existing audit-stamp tests untouched — EF Core *composes* interceptors, which is exactly what's exploited.

The mechanism is **single-pass capture-and-add inside `SavingChanges`** — no post-save second `SaveChanges`:

```csharp
List<EntityEntry> tracked = context.ChangeTracker.Entries()
    .Where(e => Monitored.Contains(e.Metadata.ClrType)
                && e.Metadata.ClrType != typeof(AuditLog)
                && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
    .ToList();
// ... build rows ...
context.Set<AuditLog>().AddRange(rows);
```

Why this works in one pass — and why it's correct — is the deepest point in the chunk:

- **Client-generated Guid PKs make a post-save hook unnecessary.** Every monitored entity (`Product`, `InventoryItem`, `Order`, `Payment`, `Shipment`) has a `Guid` PK assigned *before* the SQL runs, so `EntityId` is already known at `SavingChanges` time. Contrast a DB-generated identity key (like `AuditLog`'s own `bigint`), where you'd have to wait for `SavedChanges` to learn the key. Because the keys are known up front, the audit rows can be `Add`ed to the *same* `ChangeTracker` and **insert in the same `SaveChanges`, inside the same transaction** as the change they describe. A rolled-back business change rolls back its audit row too — atomicity for free, no orphaned trail.
- **The `.ToList()` materializes the entries *before* adding audit rows.** `AddRange` mutates the `ChangeTracker`; iterating it while mutating it would throw. Snapshot first, then add.
- **The explicit `!= typeof(AuditLog)` guard is recursion-safety.** `AuditLog` is *not* in the `Monitored` set, so the trail can't trail itself — but the guard is belt-and-suspenders in case someone later adds `AuditLog` to the allowlist, which would otherwise cause infinite self-auditing.

The **`Monitored` allowlist is exactly five types** — the entities whose mutations matter to an admin:

```csharp
private static readonly HashSet<Type> Monitored = new()
{ typeof(Product), typeof(InventoryItem), typeof(Order), typeof(Payment), typeof(Shipment) };
```

Carts, reservations, profiles, and `AuditLog` itself are deliberately *not* trailed (they're either ephemeral session state or the log itself). And **PII redaction is by property name** — the serializer replaces sensitive fields with `"***"` and skips binary columns:

```csharp
private static readonly HashSet<string> Redacted = new(StringComparer.OrdinalIgnoreCase)
{ "GuestEmail", "Email", "Password", "PasswordHash", "Token", "Secret",
  "ShippingAddress", "BillingAddress", "RawPayloadJson" };
// ...
if (prop.ClrType == typeof(byte[])) continue;                       // skip RowVersion etc.
bag[prop.Name] = Redacted.Contains(prop.Name) ? "***" : values[prop.Name];
```

The `BeforeJson`/`AfterJson` pair is built from `entry.OriginalValues` / `entry.CurrentValues`, with `BeforeJson` null on inserts and `AfterJson` null on deletes — so the JSON faithfully shows the transition direction. The `byte[]` skip means `RowVersion` (pure noise) never lands in the snapshot, and the name-based redaction keeps `GuestEmail`/addresses out of an admin-readable table per `CODING_STANDARDS`' never-log-PII rule.

#### `Common/Abstractions/IAuditWriter.cs` + `AuditWriter.cs` (resume-gold)

The *named business-action* half of the "written two ways" story. The interceptor can only emit a generic `Update` — it can't tell a refund from a plain status change. So services that perform a notable operation call `Record(...)` to stage a legible event:

```csharp
public interface IAuditWriter
{
    void Record(string action, string entityType, string entityId, object? before = null, object? after = null);
}
```

`AuditWriter` is **scoped**, so it shares the **same request `RetailDbContext`** the calling service uses — and it only `Add`s the row; it never calls `SaveChanges`:

```csharp
public void Record(string action, string entityType, string entityId, object? before = null, object? after = null)
{
    _db.Set<AuditLog>().Add(new AuditLog
    {
        Actor = _currentUser.UserId ?? "system",
        Action = action,
        // ...
        BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
        AfterJson = after is null ? null : JsonSerializer.Serialize(after),
        OccurredAt = _timeProvider.GetUtcNow(),
    });
}
```

The "no `SaveChanges`" decision is the same atomicity contract as the interceptor: the row rides the **business operation's own `SaveChanges`**, so the named-action row commits in the same transaction as the change — a rolled-back refund rolls back its `"Refund"` audit row. The actor resolves from `ICurrentUserAccessor` (null → `"system"`, the truthful value for background work, matching the interceptor's `actor` exactly), and the timestamp comes from the injected `TimeProvider`. Callers pass *small anonymous objects* of the fields that matter (never raw PII) for `before`/`after`, since this is the human-curated complement to the interceptor's full-row diff.

#### `Data/RetailDbContext.cs` + `Program.cs` wiring

The context adds the two DbSets (`Shipments`, `AuditLogs` — picked up automatically by `ApplyConfigurationsFromAssembly`, no `OnModelCreating` edit needed). The wiring that matters is in `Program.cs`, and **interceptor order is load-bearing**:

```csharp
builder.Services.AddScoped<AuditTrailInterceptor>();
builder.Services.AddScoped<IAuditWriter, AuditWriter>();
// ...
options.AddInterceptors(
    sp.GetRequiredService<AuditingInterceptor>(),     // stamps CreatedBy/UpdatedAt FIRST
    sp.GetRequiredService<AuditTrailInterceptor>());  // then snapshots the STAMPED values
```

**Interview gotcha:** `AuditingInterceptor` is registered *first* on purpose — it stamps `CreatedBy`/`UpdatedAt` before `AuditTrailInterceptor` reads `CurrentValues`, so the `AfterJson` snapshot reflects the *final* stamped values, not pre-stamp values. Both are scoped so they share the request's `DbContext` and `ICurrentUserAccessor`. (Note: the set-based `ExecuteUpdate` paths from Phase 2 still bypass *both* interceptors — those statements that hand-stamp `UpdatedBy` also produce no audit row, a known gap of the change-tracker-based approach.)

#### `Program.cs` — the policy-based `AddAuthorization` block (resume-gold)

The RBAC backbone. Three role arrays expand into 11 named policies — one per capability in the matrix:

```csharp
string[] staffPlus   = { Roles.Staff, Roles.StoreManager, Roles.Administrator };
string[] managerPlus = { Roles.StoreManager, Roles.Administrator };
string[] adminOnly   = { Roles.Administrator };

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Roles.Policies.OrdersView,    p => p.RequireRole(staffPlus));
    options.AddPolicy(Roles.Policies.OrdersFulfill, p => p.RequireRole(staffPlus));
    options.AddPolicy(Roles.Policies.InventoryAdjust, p => p.RequireRole(staffPlus));
    // ... AuditView, ReportsView → staffPlus
    options.AddPolicy(Roles.Policies.OrdersRefund,      p => p.RequireRole(managerPlus));
    options.AddPolicy(Roles.Policies.UsersManageStaff,  p => p.RequireRole(managerPlus));
    // ... AuditExport, ReportsExport → managerPlus
    options.AddPolicy(Roles.Policies.UsersManageManagers, p => p.RequireRole(adminOnly));
    options.AddPolicy(Roles.Policies.CatalogManage,       p => p.RequireRole(adminOnly));
});
```

The keys themselves live in `Roles.Policies` (in `Roles.cs`) as the single source of truth — `OrdersView = "Orders.View"`, `OrdersRefund = "Orders.Refund"`, `CatalogManage = "Catalog.Manage"`, and so on. The design rationale, straight from the `Roles.cs` doc comment:

> The matrix is **capability-shaped and overlapping** (refund = StoreManager+Administrator, fulfil = Staff+StoreManager+Administrator, manage-catalog = Administrator-only), so instead of scattering `[Authorize(Roles = "StoreManager,Administrator")]` strings across every controller, each capability is a **named policy defined ONCE**, applied as `[Authorize(Policy = Roles.Policies.X)]`. A rule change is one edit.

**Why named policies beat scattered role strings** is the interview point:

- **One edit changes a rule everywhere.** If "Staff can now refund," you flip `OrdersRefund` from `managerPlus` to `staffPlus` in *one place* — not a find-replace of `"StoreManager,Administrator"` across N controllers, where one missed string is a silent privilege bug.
- **The intent is named, not implied.** `[Authorize(Policy = Roles.Policies.OrdersRefund)]` reads as a *capability*; `[Authorize(Roles = "StoreManager,Administrator")]` makes the reader reverse-engineer *which* capability that role set is gating.
- **It needs no token change.** The JWT already emits `ClaimTypes.Role`, and `RequireRole` reads exactly those claims — so this is purely a server-side authorization-policy addition.
- **`AuditExport`/`ReportsExport` are defined-but-unused on purpose.** Export is deferred, but defining the policies now is what makes "Staff is *read-only* on audit/reports" a real tier the instant an export button exists — the read policies (`AuditView`/`ReportsView`) are `staffPlus`, the export policies are `managerPlus`, so the read/write split is already wired.

Storefront `[Authorize(Roles = Customer)]` attributes stay **role-based** — only the admin matrix is policy-based, because the storefront has no overlapping-capability problem to solve.

#### `Data/Migrations/20260617095311_0008_shipment_audit.cs`

Materializes both tables. The `AuditLog` PK is the SQL identity, and the `Shipment` indexes carry the cardinality:

```csharp
Id = table.Column<long>(type: "bigint", nullable: false)
    .Annotation("SqlServer:Identity", "1, 1"),     // AuditLog clustered identity
// ...
Status = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)1),  // Shipment.Pending
// ...
migrationBuilder.CreateIndex(name: "UX_Shipment_OrderId", table: "Shipment",
    column: "OrderId", unique: true);
migrationBuilder.CreateIndex(name: "IX_Shipment_TrackingNumber", table: "Shipment",
    column: "TrackingNumber", filter: "[TrackingNumber] IS NOT NULL");
```

Note the migration confirms the conventions hold end-to-end: `ShipmentStatus` lands as `tinyint NOT NULL DEFAULT 1` (the poison-`0`-sentinel discipline), the three `AuditLog` search indexes are all created, and the `Shipment → Order` FK is `ReferentialAction.Cascade`. This is a clean additive migration — two new tables, no alterations to existing ones (the `Order.Shipment` nav is the *inverse* side of the FK that lives on `Shipment`, so `Order`'s table is unchanged).

### Chunk 0 — what to know cold

1. **`ShipmentStatus` is the 5th `: byte`→`tinyint` enum** (Pending=1→Shipped=2→Delivered=3, default 1). The finer logistics lifecycle lives on `Shipment` so **`OrderStatus` is never renumbered** — "Mark Shipped" just sets the existing `Fulfilled`; no new order status was added.
2. **`Shipment` is 1:0..1 to `Order`**, enforced by the unique `UX_Shipment_OrderId`; dropping that index is the clean path to multi-shipment. It **is** `IAuditableEntity`; the filtered `IX_Shipment_TrackingNumber` (`WHERE TrackingNumber IS NOT NULL`, non-unique) serves tracking lookups without indexing null `Pending` rows.
3. **`AuditLog` is a `bigint`-IDENTITY append-only ledger and deliberately NOT `IAuditableEntity`** — a write-once log has no "who updated this" story; the narrow monotonic clustered key avoids the fragmentation a random GUID would cause (same reasoning as `ProcessedStripeEvent`). Three indexes: `OccurredAt`, `(EntityType, EntityId)`, `(Actor, OccurredAt)`.
4. **Two `SaveChanges` interceptors, two jobs**: `AuditingInterceptor` *stamps* columns; `AuditTrailInterceptor` *appends* an `AuditLog` row. They're separate for single-responsibility, and registered **stamp-first** so the trail snapshots the final stamped values.
5. **`AuditTrailInterceptor` is single-pass capture-and-add in `SavingChanges`** — possible only because monitored entities have **client-generated Guid PKs** (key known pre-SQL), so audit rows insert in the **same transaction** (atomic rollback). It snapshots entries to a list before `AddRange` (can't mutate while iterating) and explicitly excludes `AuditLog` (self-trail guard).
6. **The `Monitored` allowlist is {Product, InventoryItem, Order, Payment, Shipment}**; redaction is **by property name** (`GuestEmail`, `Email`, addresses, secrets → `"***"`) and `byte[]` columns (RowVersion) are skipped — keeps PII out of an admin-readable table.
7. **`IAuditWriter`/`AuditWriter` write named business-action rows** ("Refund"/"Shipped"/"InventoryAdjusted") on the **shared scoped `RetailDbContext`**, actor from `ICurrentUserAccessor` (null → "system"), and **never call `SaveChanges`** — the row rides the caller's unit of work, atomic with the change.
8. **The policy block expands 3 role arrays (`staffPlus`/`managerPlus`/`adminOnly`) into 11 named policies** in `Roles.Policies`, each a `RequireRole(...)`. Named policies beat scattered `[Authorize(Roles="...")]` strings: a rule change is one edit, intent is named, and it needs no JWT change (the token already emits `ClaimTypes.Role`). Storefront `Customer` attributes stay role-based; only the admin matrix is policy-based.

---

## 3. Chunk 1 — RBAC enforcement, the AdminShell, thin user management

Story 3.1 turns the back office on. Phase 1 seeded four role *names* (`Roles.cs`) but the only thing guarding admin writes was a blunt `[Authorize(Roles = Roles.Administrator)]`. Chunk 1 replaces that with a **capability matrix expressed as named policies**, then ships the first endpoint that actually *consumes* the new tiering — a thin user-management surface where a StoreManager can mint Staff but only an Administrator can mint another StoreManager. The interview meat is the split: **what a static policy can enforce vs what has to be a body-dependent in-handler check**, and why the in-handler check is race-free.

> The React side of this chunk — the role-driven `AdminShell` that renders "three roles, three sidebars" — is detailed in **§7**. This section is backend only; cross-reference §7 for the UI that the demo Staff/StoreManager accounts log into.

### What is in Chunk 1 (backend)

```
src/api/Retail.Api/
├─ Common/Constants/Roles.cs               ← +Roles.Policies.* — the named-policy capability matrix
├─ Program.cs                              ← AddAuthorization: each capability = one RequireRole policy
├─ Controllers/CatalogController.cs        ← every write migrated [Authorize(Roles=…)] → [Authorize(Policy=CatalogManage)]
├─ Identity/IdentityDataSeeder.cs          ← +config-gated, non-prod demo Staff/StoreManager seeding
├─ Controllers/AdminUsersController.cs     ← [Authorize(Policy=UsersManageStaff)] list+create; the in-handler SM rule
├─ Services/IAdminUserService.cs / AdminUserService.cs   ← list (paged) + create over Identity's UserManager
├─ DTOs/Requests/CreateUserRequest.cs      ← Email/Password/DisplayName/Role record
├─ DTOs/Requests/AdminUserListQuery.cs     ← Role filter + Page/PageSize (defaults 1/20)
├─ DTOs/Responses/AdminUserDto.cs          ← Id/Email/DisplayName/Roles projection (no PII beyond email)
└─ Validators/CreateUserRequestValidator.cs ← email + ≥12-char policy + Role ∈ {Staff, StoreManager}
```

### Per-file purpose

#### `Common/Constants/Roles.Policies` + `Program.cs` (the policy matrix) — resume-gold

The structural move of the chunk. Phase 1 left `Roles.cs` holding only the four role *names*; Chunk 1 adds a nested `Policies` class — a constant per **capability**, not per role:

```csharp
public const string OrdersRefund = "Orders.Refund";          // StoreManager + Administrator
public const string UsersManageStaff = "Users.ManageStaff";  // StoreManager + Administrator
public const string CatalogManage = "Catalog.Manage";        // Administrator only
```

The matrix is **defined once** in `Program.cs`, where three role arrays fan out into `RequireRole` policies:

```csharp
string[] staffPlus = { Roles.Staff, Roles.StoreManager, Roles.Administrator };
string[] managerPlus = { Roles.StoreManager, Roles.Administrator };
string[] adminOnly = { Roles.Administrator };
// ...
options.AddPolicy(Roles.Policies.OrdersRefund, p => p.RequireRole(managerPlus));
options.AddPolicy(Roles.Policies.UsersManageStaff, p => p.RequireRole(managerPlus));
options.AddPolicy(Roles.Policies.CatalogManage, p => p.RequireRole(adminOnly));
```

**Why named policies instead of `[Authorize(Roles = "StoreManager,Administrator")]` everywhere?** The matrix is *capability-shaped and overlapping*: refund = manager+, fulfil = staff+, manage-catalog = admin-only. Encoding that as role-string literals scatters the same `"StoreManager,Administrator"` across a dozen attributes — a rule change ("let Staff issue refunds") becomes a find-replace, and a typo in any one string is an invisible authorization hole that still compiles. With named policies, the role→capability mapping lives in **one `AddAuthorization` block**; a controller only ever references an opaque key (`[Authorize(Policy = Roles.Policies.OrdersRefund)]`), and a rule change is a single edit. **Interview note:** this needs *zero* token change — the JWT already emits `ClaimTypes.Role`, so `RequireRole` reads claims that are already in the cookie. The storefront `[Authorize(Roles = Customer)]` attributes stay role-based on purpose; only the admin matrix went policy-based. Two policies (`AuditExport`/`ReportsExport`) are defined but unused in the MVP — they make the "Staff is read-only on audit/reports" tier *real* the moment an export button exists.

#### `Controllers/CatalogController.cs` — the migration to one source of truth — resume-gold

Every catalog **write** in this controller — create/update/delete product, the five image endpoints, the variant endpoints, the two admin reads — moved from the blunt role check to the capability:

```csharp
[HttpPost("products")]
[Authorize(Policy = Roles.Policies.CatalogManage)]
```

The git diff is exactly ten `[Authorize(Roles = Roles.Administrator)]` → `[Authorize(Policy = Roles.Policies.CatalogManage)]` substitutions, behaviour-preserving (both resolve to admin-only today). **Why bother if the result is identical?** Because the *meaning* changed: the attribute now says "this action requires the catalog-management **capability**," and the capability's role set is defined in one place. If catalog management later opens to StoreManager, that's a one-line edit in `Program.cs` — no controller is touched, and there's no risk of catching nine of ten endpoints in a find-replace. This is the "one source of truth" payoff made concrete: the controller stops *encoding policy* and starts *referencing* it. (The public storefront reads — `ListProducts`, `GetProductBySlug`, `ListCategories` — keep their `[AllowAnonymous]`; only the admin surface tiers.)

#### `Identity/IdentityDataSeeder.cs` — config-gated, non-production demo accounts — resume-gold

The seeder gained the demo back-office accounts the "three roles, three sidebars" demo logs into. The gate is **belt-and-braces**:

```csharp
await SeedDefaultAdminAsync();

// ...never in Production, regardless of what config is present.
if (!_env.IsProduction())
{
    await SeedDemoAccountAsync("Auth:DemoStaff", Roles.Staff);
    await SeedDemoAccountAsync("Auth:DemoManager", Roles.StoreManager);
}
```

And each demo account is itself credential-gated — passwords come from config (`Auth:DemoStaff:Password` etc., supplied via User Secrets / Key Vault), never source:

```csharp
string? email = _config[$"{section}:Email"];
string? password = _config[$"{section}:Password"];
// ...
if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
{
    _logger.LogDebug("Demo {Role} account not seeded: {Section} Email/Password not configured.", role, section);
    return;
}
```

**Two independent guards, by design.** The `!_env.IsProduction()` check means even a misconfigured prod deploy that *does* carry `Auth:DemoManager:*` config can never mint a seeded StoreManager — a demo account with a known-shaped password is a standing privilege-escalation risk in prod, so the environment guard is unconditional. The "skip if credentials unset" check means a fresh clone with no secrets still boots (catalog + cart work) without seeding guessable accounts. The same pattern guards the real admin (`SeedDefaultAdminAsync` skips with a warning if `Auth:DefaultAdmin:Password` is blank). **Interview gotcha:** the seeder *throws* on a hard Identity failure (role create / role grant) so a broken seed fails fast at boot — but *skips* (doesn't throw) on missing credentials, because "no demo password configured" is a normal state, not an error. And it logs the new account's `UserId`, never the email — the email is a sensitive field per CODING_STANDARDS.

#### `Controllers/AdminUsersController.cs` — the static policy + the body-dependent rule — resume-gold

The first endpoint to *use* the tiering. The whole controller is gated at the class level on the StoreManager+ capability:

```csharp
[Route("api/v1/admin/users")]
[Authorize(Policy = Roles.Policies.UsersManageStaff)]
public sealed class AdminUsersController : ControllerBase
```

That static policy answers "may this caller manage staff at all?" But the *real* rule — "a StoreManager may create Staff, but only an Administrator may create another StoreManager" — **depends on the request body**, so it lives in the handler:

```csharp
// A StoreManager may create Staff, but only an Administrator may create another StoreManager.
if (request.Role == Roles.StoreManager && !User.IsInRole(Roles.Administrator))
{
    return StatusCode(
        StatusCodes.Status403Forbidden,
        ApiResponse.Fail("Only an Administrator can create a StoreManager account."));
}
```

**Why can't a static policy express this?** A `RequireRole`/`RequireClaim` policy is evaluated against the caller's identity *before the handler runs* — it has no access to the bound request body, so it can't say "the answer depends on which `Role` the body asks for." The rule is genuinely a function of *(caller's role, requested role)*, a two-input predicate; the policy can only check one input. So `UsersManageStaff` gates the door (both SM and Admin get in), and the handler adds the body-dependent leaf check. **Interview gotcha — why is this race-free?** `User.IsInRole(Roles.Administrator)` reads the **role claim baked into the validated JWT** that already authenticated this request. It is not a database round-trip, so there's no read-then-act window an attacker could widen — the authority was fixed the instant the token was minted and the cookie validated. A DB-backed `await userManager.IsInRoleAsync(...)` here would reintroduce a TOCTOU gap; reading the claim off the principal does not. The `ListUsers` GET sits under the same class policy and just forwards the role filter + paging to the service.

#### `Services/AdminUserService.cs` + `IAdminUserService.cs` — thin create/list over `UserManager` — resume-gold

A deliberately thin wrapper over ASP.NET Identity's `UserManager<ApplicationUser>`. The division of labour is the headline, stated in the interface XML-doc: **"The CALLER's authority (who may create which role) is enforced at the controller, not here."** The service never asks "are you allowed?" — it just performs the requested create/list. Create pre-checks the email for a friendly 409, then leans on Identity's unique index as the race-proof backstop:

```csharp
// Pre-check for a friendly 409; Identity's own unique-index is the race-proof backstop.
if (await _userManager.FindByEmailAsync(request.Email) is not null)
{
    throw new ConflictException($"An account with email '{request.Email}' already exists.");
}
// ...
EmailConfirmed = true, // an admin is vouching for this account
```

New accounts are created `EmailConfirmed = true` because an admin minting the account *is* the verification — there's no confirmation-email round-trip for a back-office user.

**Two design notes worth knowing cold.** First, **`AdminUserService` is deliberately NOT in the audit-monitored set.** The class XML-doc spells it out: `AuditTrailInterceptor` monitors Product/Inventory/Order/Payment/Shipment (REQUIREMENTS §11.1), and account rows are excluded on purpose — partly because user CRUD isn't in the audited domain, partly because auditing Identity rows would serialise Identity's email / normalized-email / password-hash columns into the trail (a PII leak into the audit log). The `AuditingInterceptor` (timestamps/`CreatedBy`) still stamps `CreatedBy` with the acting admin's id; only the *before/after diff* audit trail is skipped.

Second, **the SQL-paging fix in `ListAsync`.** The unfiltered path pages *in SQL* so the whole user table is never materialised:

```csharp
if (string.IsNullOrWhiteSpace(role))
{
    // No role filter → page in SQL (COUNT + OFFSET/FETCH) so we never materialise the whole
    // user table just to return one page.
    IQueryable<ApplicationUser> ordered = _userManager.Users.OrderBy(u => u.Email);
    total = await ordered.CountAsync(ct);
    pageItems = await ordered.Skip((safePage - 1) * safeSize).Take(safeSize).ToListAsync(ct);
}
else
{
    // Identity's role lookup has no IQueryable/paged variant, so it returns everyone in the
    // role; page that (smaller, role-bounded) set in memory.
    IReadOnlyList<ApplicationUser> inRole = (await _userManager.GetUsersInRoleAsync(role))
        .OrderBy(u => u.Email).ToList();
    ...
}
```

**Interview gotcha:** the two branches page differently *because Identity forces it*. `_userManager.Users` is `IQueryable`, so `Skip/Take` translate to `OFFSET/FETCH` and only one page leaves SQL Server. But `GetUsersInRoleAsync` has no paged/queryable variant — it returns *everyone* in the role as a materialised list — so that (smaller, role-bounded) set is paged in memory. The honesty in the comments: `GetRolesAsync` per user is an acknowledged N+1, accepted because the back-office user count is tiny; a join over `AspNetUserRoles` is the noted optimisation if it ever grows. `page` is clamped to `≥1` and `pageSize` to `1..100` so a hostile `pageSize=100000` can't force a giant fetch.

#### `CreateUserRequest` + `CreateUserRequestValidator` + `AdminUserListQuery` + `AdminUserDto`

The DTO is a flat record — `CreateUserRequest(string Email, string Password, string DisplayName, string Role)` — and the validator is where the **role allow-list** lives:

```csharp
RuleFor(x => x.Role)
    .Must(r => r == Roles.Staff || r == Roles.StoreManager)
    .WithMessage($"Role must be '{Roles.Staff}' or '{Roles.StoreManager}'.");
```

**Why restrict to `{Staff, StoreManager}` at the validator?** It closes a privilege-escalation surface: without it, a caller could post `Role = "Administrator"` (or `"Customer"`) and — if the controller's leaf check were ever weakened — mint an admin. The validator makes "create an Administrator via this endpoint" structurally impossible (a 422), and the controller's `request.Role == Roles.StoreManager` check then layers the finer SM-vs-Staff authority on top. The password rule mirrors self-registration exactly (≥12 chars, a letter, a digit) so back-office accounts aren't held to a weaker bar than shoppers. `AdminUserListQuery` carries the optional `Role` filter + `Page`/`PageSize` (defaults `1`/`20`). `AdminUserDto(Id, Email, DisplayName, Roles)` is a tight projection — it exposes the email (needed to identify the account) but none of Identity's hash/security-stamp/normalized columns, so the list endpoint can't leak credential material.

### Chunk 1 — what to know cold

1. **The admin matrix is named policies, defined once.** `Roles.Policies.*` constants + one `AddAuthorization` block in `Program.cs` (`staffPlus`/`managerPlus`/`adminOnly` → `RequireRole`). A rule change is one edit; controllers reference an opaque key. Needs no JWT change — `RequireRole` reads the role claim already in the token. Storefront `Customer` attributes stay role-based.
2. **Catalog writes migrated `[Authorize(Roles=Administrator)]` → `[Authorize(Policy=CatalogManage)]`** (10 endpoints) — behaviour-identical today, but the controller now *references* policy instead of *encoding* it, so re-tiering catalog management is one line.
3. **`AdminUsersController` is gated `[Authorize(Policy = UsersManageStaff)]`** (StoreManager + Administrator) at the class level — that's the static "may you manage staff at all?" check.
4. **"Only an Administrator may create a StoreManager" is an in-handler check** (`request.Role == StoreManager && !User.IsInRole(Administrator)` → 403) because it's a function of *(caller role, requested role)* — a body-dependent two-input predicate a static policy can't express. It's **race-free** because `User.IsInRole` reads the **JWT role claim**, not the DB (no TOCTOU window).
5. **Demo Staff/StoreManager seeding is double-gated** — `!_env.IsProduction()` (unconditional) *and* "skip if `Auth:DemoStaff`/`DemoManager` credentials unset"; passwords come from User Secrets, never source; the seeder logs `UserId`, not email.
6. **`AdminUserService` is a thin wrapper over `UserManager`** — controller owns authority, service just creates/lists; new accounts are `EmailConfirmed = true` (an admin vouches); friendly-409 email pre-check backed by Identity's unique index.
7. **Account rows are deliberately excluded from the audit-monitored set** (Product/Inventory/Order/Payment/Shipment only) — avoids serialising Identity's PII columns into the trail; `CreatedBy` is still stamped by the timestamp interceptor.
8. **The SQL-paging fix:** the no-filter path pages in SQL (`IQueryable` → `OFFSET/FETCH`, never materialises the whole table); the role-filter path *must* page in memory because `GetUsersInRoleAsync` has no queryable variant. `page`/`pageSize` clamped (`≥1`, `1..100`).
9. **The validator caps `Role ∈ {Staff, StoreManager}`** — making "create an Administrator/Customer via this endpoint" a structural 422, closing a privilege-escalation surface — and reuses the ≥12-char self-registration password policy.

---

## 4. Chunk 2 — The order workbench (read + fulfilment + admin refund)

If Phase 2 let a *shopper* buy, this chunk is where **staff finally act on what they bought**. It is the first place the 3-role matrix from Chunk 1 gets pointed at money: read every order (not just your own), push a Paid order through ship → deliver, and — the hard one — issue an **admin refund** that has to hit Stripe exactly once even under a concurrent customer-cancel. The whole chunk is a study in *re-using* the Phase-2 refund machinery (the `Refunding` transient claim, `OrderRefundService`, `Order.RowVersion`) from a *second* entry point without ever double-charging or double-restocking.

The shape is deliberately the same clean four-layer slice as Phase 2 — controller translates HTTP, service owns every rule + transition, repository is the EF/concurrency primitive, mappers/DTOs are the read boundary — but now the writes are **status transitions guarded per-action**: ship and refund ride `Order.RowVersion`, deliver is a benign last-write-wins.

### What is in Chunk 2

```
src/api/Retail.Api/
├─ Controllers/AdminOrdersController.cs      ← GET list/detail + POST ship/deliver/refund; the Orders.* policies; DateRangeGuard 422
├─ Services/IAdminOrderService.cs            ← the 5-method workbench contract
├─ Services/AdminOrderService.cs (resume-gold) ← list/get + MarkShipped/MarkDelivered/Refund; ParseStatus; the TOCTOU refund dance
├─ Services/OrderRefundService.cs (resume-gold) ← the SHARED idempotent reversal — records the named "Refund" + "Restocked" rows at the real transition
├─ Repositories/IOrderRepository.cs / OrderRepository.cs   ← admin reads + the claim/release ExecuteUpdate primitives
├─ Mappers/AdminOrderMappers.cs              ← Order → admin DTOs (surfaces email + payment ledger + shipment)
├─ DTOs/Responses/  AdminOrderSummaryDto, AdminOrderDetailDto, PaymentDto, ShipmentDto
├─ DTOs/Requests/   AdminOrderListQuery (status/from/to/customerEmail + paging), MarkShippedRequest (carrier + tracking)
├─ Validators/MarkShippedRequestValidator.cs ← carrier ≤ 60, tracking ≤ 120, both required
└─ Common/Validation/DateRangeGuard.cs       ← shared reversed-range 422 (audit §7 / orders §8 / report §9)
```

### Per-file purpose

#### `Controllers/AdminOrdersController.cs` (resume-gold)

A thin `[Route("api/v1/admin/orders")]` boundary that does three jobs and nothing else: pick the **policy** per action, run the **422 guards** (FluentValidation for the ship body, `DateRangeGuard` for the list), and wrap everything in `ApiResponse<T>`. The headline is that authorization is **capability-scoped, not blanket-admin** — three different policies on five endpoints:

```csharp
[HttpGet]               [Authorize(Policy = Roles.Policies.OrdersView)]    // list
[HttpGet("{id:guid}")]  [Authorize(Policy = Roles.Policies.OrdersView)]    // detail
[HttpPost("{id:guid}/ship")]    [Authorize(Policy = Roles.Policies.OrdersFulfill)]
[HttpPost("{id:guid}/deliver")] [Authorize(Policy = Roles.Policies.OrdersFulfill)]
[HttpPost("{id:guid}/refund")]  [Authorize(Policy = Roles.Policies.OrdersRefund)]
```

**Why named policies, not `[Authorize(Roles = "StoreManager,Administrator")]` strings?** The capability matrix is *overlapping* — `OrdersView` and `OrdersFulfill` are `Staff + StoreManager + Administrator`, but `OrdersRefund` is **`StoreManager + Administrator` only** (Staff fulfils orders but cannot move money back out). Each capability is a single `RequireRole(...)` policy defined *once* in `Program.cs`, so a rule change is one edit, not a grep-and-replace across controllers. The interview point: a `Staff` who can mark-shipped gets a **403 on `/refund`** — and the controller doesn't have to know which roles satisfy the policy, only which *capability* the action needs.

The **`DateRangeGuard` 422** runs before the service is ever touched:

```csharp
if (DateRangeGuard.Validate(query.From, query.To) is { } invalid)
{
    return UnprocessableEntity(invalid);
}
```

The guard exists because a reversed range (`from > to`) compiles into an always-false EF predicate that returns an empty page with a **200** — a *silent* client bug that reads as "no orders." Rejecting it up front with a 422 (`Field = "from"`) turns a confusing empty result into an actionable error. It is the same shared guard the audit viewer (§7) and sales report (§9) use, which is why it lives in `Common/Validation` rather than inline. Note the controller passes no `maxSpanDays` — the order list is indexed on `PlacedAt`, so only the report needs the span cap.

The **per-action concurrency contract** is documented right in the class summary and is worth memorising: *ship and refund are RowVersion-guarded (a stale write → 409); deliver is an idempotent shipment update where a concurrent double-deliver is benign, last-write-wins.* That asymmetry is the whole design — not every transition needs a token, only the ones two writers can race into corruption.

#### `Services/IAdminOrderService.cs`

The five-method contract — `ListAsync`, `GetAsync`, `MarkShippedAsync`, `MarkDeliveredAsync`, `RefundAsync` — and the doc comments encode the **409 rules** so they're part of the contract: ship is "409 if not Paid / already shipped," deliver is "409 if not shipped," refund is "409 otherwise." Reads return `AdminOrder*Dto`; every write returns the **full refreshed `AdminOrderDetailDto`**, so the SPA re-renders from authoritative server state rather than optimistically guessing the new status.

#### `Services/AdminOrderService.cs` (resume-gold)

The brain of the chunk. Four things to know cold.

**1. `ListAsync` clamps paging, then delegates the filter SQL — and `ParseStatus` is the subtle bit.**

```csharp
int safeSize = Math.Clamp(query.PageSize, 1, 100);
(IReadOnlyList<Order> items, int total) = await _orders.GetPagedForAdminAsync(
    ParseStatus(query.Status), query.From, query.To, query.CustomerEmail, safePage, safeSize, ct);
```

The status filter arrives as a **string** (`"Paid"`) and must become an `OrderStatus?`. The naive `Enum.TryParse` has a trap:

```csharp
private static OrderStatus? ParseStatus(string? status) =>
    Enum.TryParse(status, ignoreCase: true, out OrderStatus parsed) && Enum.IsDefined(parsed)
        ? parsed
        : null;
```

**Interview gotcha — why `Enum.IsDefined` is mandatory.** `Enum.TryParse` happily *succeeds* on `"99"` (or a flags-style `"Paid, Refunded"`) because the underlying type is a `byte` and any in-range integer round-trips — it would return `(OrderStatus)99`, which the repository then filters on, yielding an **always-empty page** that looks like "no Paid orders" rather than "bad filter." `Enum.IsDefined` rejects the in-range-but-undefined value, and the method falls back to `null` = "all statuses." A blank/garbage filter is treated *leniently* (it's a filter, not a command) — the right call for a read endpoint, but only safe because `IsDefined` stops the silent-empty-page failure mode.

**2. `MarkShippedAsync` — one TRACKED write does the status flip + the shipment insert, and the interceptor auto-audits both.**

It loads the order *tracked* (with its shipment), gate-checks twice (must be `Paid`; must not already have a shipment → `ConflictException` → 409), then mutates and saves **once**:

```csharp
order.Shipment = new Shipment { OrderId = order.Id, Carrier = request.Carrier,
    TrackingNumber = request.TrackingNumber, Status = ShipmentStatus.Shipped, ShippedAt = now };
order.Status = OrderStatus.Fulfilled;

_audit.Record("Shipped", nameof(Order), order.Id.ToString(),
    before: new { Status = nameof(OrderStatus.Paid) },
    after:  new { Status = nameof(OrderStatus.Fulfilled), request.Carrier, request.TrackingNumber });

await _orders.SaveChangesAsync(ct);
```

Three design points fuse here. First, **one `SaveChanges`** makes "create the Shipment + flip Paid→Fulfilled" atomic — you can never end up Fulfilled-with-no-shipment or shipment-with-a-Paid-order. Second, because this is a *tracked* write (not a set-based `ExecuteUpdate`), the `AuditTrailInterceptor` automatically records the Order-update and Shipment-insert rows — so the **explicit `_audit.Record("Shipped", …)`** is *additive*: a single legible business-event row on top of the mechanical field-diffs, the thing a human reads in the audit log. Third — and this is the resume line — **`Order.RowVersion` makes the flip concurrency-safe for free.** Two admins clicking "ship" at once: SaveChanges carries the `RowVersion` each loaded; the engine re-stamps it on UPDATE, so the second writer matches the *old* token, EF throws `DbUpdateConcurrencyException`, and the middleware maps it to a **409** — instead of two shipments racing onto one order. The `order.Shipment is not null` pre-check is the friendly common-case 409; `RowVersion` is the backstop for the true race the pre-check can't see.

**Why the two-tier status split?** Notice the order goes to `OrderStatus.Fulfilled` (one coarse step) while the finer `Pending → Shipped → Delivered` lives on `ShipmentStatus`. `OrderStatus` is a *stored, serialized contract we don't renumber* — so logistics granularity is pushed onto the Shipment, and `OrderStatus` never grows a `Shipped`/`Delivered` member.

**3. `MarkDeliveredAsync` — deliver advances the SHIPMENT only; the order stays Fulfilled.**

```csharp
order.Shipment.Status = ShipmentStatus.Delivered;
order.Shipment.DeliveredAt = now;
_audit.Record("Delivered", nameof(Shipment), order.Shipment.Id.ToString(), …);
```

It guards on "has a shipment" and "shipment is currently `Shipped`" (else 409). This is the one write the controller called out as *not* RowVersion-critical: a concurrent double-deliver just sets the same `Delivered`/`DeliveredAt` twice — idempotent, benign, last-write-wins — so paying the cost of a concurrency token here would be ceremony. The audit row keys on `nameof(Shipment)`, not `Order`, because the shipment is what actually changed.

**4. `RefundAsync` — the centerpiece: Paid-ONLY, re-drivable from `Refunding`, with a TOCTOU claim around the Stripe call.**

This is the file's hardest 80 lines and the strongest interview artifact in the chunk. The state gate is the first lesson:

```csharp
if (order.Status is not (OrderStatus.Paid or OrderStatus.Refunding))
{
    throw new ConflictException(
        $"Order #{order.OrderNumber} can't be refunded in its current state ({order.Status}).");
}
```

**Why `Paid` ONLY, not `Fulfilled`?** A shipped order is a **return/RMA case (deferred)** — refunding it would restock goods that are physically with the customer and strand the live shipment. So the admin refund deliberately covers only the not-yet-shipped Paid order. (The interface XML-doc says "Paid/Fulfilled" but the implementation and the `TryClaimForRefundByIdAsync` comment are the source of truth: **Paid only** in Phase 3 — the doc comment is aspirational.) The *second* accepted state, `Refunding`, is the clever part: it is a **RECOVERY state**. A prior attempt that succeeded at Stripe but crashed before the local reversal finished leaves the order stuck in `Refunding`; re-calling `RefundAsync` is *safe and re-drivable* because every downstream step is idempotent.

The **TOCTOU guard** — the bit that guarantees the payment provider is hit at most once — is a set-based claim done *before* touching Stripe:

```csharp
if (order.Status == OrderStatus.Paid)
{
    if (!await _orders.TryClaimForRefundByIdAsync(orderId, now, actor, ct))
        throw new ConflictException($"Order #{order.OrderNumber} can't be refunded in its current state.");
    justClaimed = true;
    _audit.Record("RefundClaimed", nameof(Order), orderId.ToString(),
        before: new { Status = nameof(OrderStatus.Paid) },
        after:  new { Status = nameof(OrderStatus.Refunding) });
}
```

`TryClaimForRefundByIdAsync` is a single atomic `UPDATE … WHERE Status = Paid` (below) — **exactly one writer flips Paid→Refunding and wins**; a concurrent customer-cancel (or a second admin) sees a non-Paid row, matches 0 rows, and gets a 409. The order is now *claimed* before a single cent moves, so Stripe is reached once. A `Refunding` order **skips** the claim (it's already claimed — the recovery path), which is why `justClaimed` is tracked.

The **rollback on Stripe failure** is the symmetric half:

```csharp
try { await _refundGateway.RefundAsync(paymentIntentId, ct); }   // idempotent key refund:{pi}
catch
{
    if (justClaimed)
    {
        await _orders.ReleaseRefundClaimToAsync(orderId, OrderStatus.Paid, …, actor, ct);
        _audit.Record("RefundClaimReleased", nameof(Order), orderId.ToString(),
            before: new { Status = nameof(OrderStatus.Refunding) },
            after:  new { Status = nameof(OrderStatus.Paid) });
        await _orders.SaveChangesAsync(ct);   // rollback leaves no other write to flush the staged audit rows
    }
    throw;
}
```

If Stripe fails *before money moved* and **we** just claimed, the order is reverted Refunding→Paid so it stays refundable; a *recovery* attempt (which didn't claim) is left `Refunding` (still re-drivable). The `await SaveChangesAsync` inside the catch is load-bearing: the `RefundClaimed`/`RefundClaimReleased` audit rows were *staged* but nothing else writes on the rollback path, so they'd be lost without an explicit flush.

**Where the real money-transition is recorded** is the most interview-sharp detail. On Stripe success, `RefundAsync` does **not** record the "Refund" row itself — it delegates:

```csharp
await _orderRefund.RefundByPaymentIntentAsync(paymentIntentId, ct);
await _orders.SaveChangesAsync(ct);   // flush any staged audit row the reversal didn't (no-op short-circuit case)
```

The named `Refund` business-event row and the per-variant `Restocked` rows are recorded **inside `OrderRefundService`, at the actual idempotent transition, exactly once, with the TRUE prior status** — not here. The reason: the admin path and the `charge.refunded` *webhook* path both funnel into the same reversal, and recording the transition at the single real transition point means a recovery re-drive (or a webhook arriving after the admin refund) can't *duplicate* the Refund row, and the row never *misreports* a `Refunding→Refunded` as `Paid→Refunded`. The audit truth lives where the state actually changes, once.

**A null-safe actor:** `string actor = _currentUser.UserId ?? "system";` — an admin refund always has a real user id, but the `?? "system"` keeps the stamp honest if somehow principal-less.

#### `Services/OrderRefundService.cs` (resume-gold)

The **shared, idempotent reversal** — the single place Refunding/Paid → Refunded actually happens, called by *both* the Phase-2 `charge.refunded` webhook and the Phase-3 admin refund. Its existence is why the admin path above could stay thin. The idempotency is two guards at the top:

```csharp
Order? order = await _orders.GetByPaymentIntentIdAsync(paymentIntentId, ct);
if (order is null) { _logger.LogWarning(…); return; }      // unknown intent → no-op
if (order.Status == OrderStatus.Refunded) { return; }       // already reversed → no-op
```

Then it captures the **true prior status** *before* mutating — this is the value the named Refund row will carry:

```csharp
OrderStatus priorStatus = order.Status; // Paid (Stripe refund) or Refunding (cancel claim)
```

It restocks each line (a set-based `RestockByVariantAsync`, so it emits an **explicit `Restocked` audit row** per line — keyed on the `InventoryItem` id resolved in one batched query, matching how a manual stock adjustment keys its audit), flips to `Refunded`, appends a **negative `Payment`** (`AmountCents = -order.TotalCents` — the ledger is signed), and records the one Refund row — all inside `BeginTransactionAsync` + a single `SaveChanges` + `CommitAsync`:

```csharp
order.Status = OrderStatus.Refunded;
order.Payments.Add(new Payment { … AmountCents = -order.TotalCents, Currency = "AUD", Status = PaymentStatus.Refunded });
_audit.Record("Refund", nameof(Order), order.Id.ToString(),
    before: new { Status = priorStatus.ToString() },
    after:  new { Status = nameof(OrderStatus.Refunded) });
```

**Why is double-restock impossible under concurrency?** Two applies (admin refund + webhook arriving together) are serialized by **`Order.RowVersion` on the tracked `SaveChanges`** — the stale writer affects 0 rows, throws `DbUpdateConcurrencyException` → 409, and never re-restocks. The `Status == Refunded` early-return catches the already-committed case; `RowVersion` catches the genuine in-flight race. Together they make the reversal safe to call from any number of entry points.

#### `Repositories/OrderRepository.cs` + `IOrderRepository.cs`

Pure EF data access split into **admin reads** and the **claim/release concurrency primitives**.

`GetPagedForAdminAsync` is the workbench list — `AsNoTracking`, *not* owner-scoped (staff see every order), with the filters composed conditionally so an absent filter adds no `WHERE`. The **customer-email filter is the join worth knowing**, because a buyer's email lives in two different places:

```csharp
query = query.Where(o =>
    (o.GuestEmail != null && o.GuestEmail.Contains(email)) ||
    (o.CustomerProfile != null && o.CustomerProfile.User != null
        && o.CustomerProfile.User.Email != null && o.CustomerProfile.User.Email.Contains(email)));
```

A **guest order carries the email directly** (`Order.GuestEmail`), but a **member order's email lives on the Identity user behind the profile** (`CustomerProfile.User.Email`) — the Chunk-0 guest-XOR-member identity invariant surfacing at the read layer. The `OR` matches both with one substring filter; the eager `.Include(o => o.CustomerProfile).ThenInclude(p => p!.User)` is what lets the mapper then resolve the displayed email without an N+1. Date bounds are deliberately **inclusive-from / exclusive-to** (`>= from`, `< to`) — the canonical half-open interval that avoids the midnight off-by-one.

`GetDetailForAdminAsync` is the same read with the **full graph** — `Lines + Payments + Shipment + CustomerProfile.User` — because the admin detail surfaces the payment *ledger* and shipment the customer view hides.

`GetTrackedWithShipmentAsync` is the **only tracked read** here — it backs the ship/deliver writes (`Include(o => o.Shipment)`, no `AsNoTracking`) so the service can mutate and `SaveChanges` with the `RowVersion` token live. The admin reads are `AsNoTracking`; the writes need tracking — the split is intentional.

The two **claim/release primitives** are the optimistic-concurrency core, set-based `ExecuteUpdate`s:

```csharp
// TryClaimForRefundByIdAsync — admin-scoped (no owner filter), Paid ONLY:
int affected = await _db.Orders
    .Where(o => o.Id == orderId && o.Status == OrderStatus.Paid)
    .ExecuteUpdateAsync(s => s
        .SetProperty(o => o.Status, OrderStatus.Refunding)
        .SetProperty(o => o.UpdatedAt, now)
        .SetProperty(o => o.UpdatedBy, actor), ct);
return affected == 1;
```

**Interview gotcha — the claim is the TOCTOU guard, and `affected == 1` IS the winner test.** SQL Server serializes the row update; a second concurrent writer sees `Status != Paid` and matches **0 rows**, so it loses. This is the *exact same pattern* as `InventoryReservationRepository.TryReserveAsync` from Phase-2 Chunk 2 (count-the-affected-rows, not catch-an-exception) — here applied to a status flip instead of a stock bump. Note there are **two** claim methods: `TryClaimForRefundByIdAsync` (admin, no owner filter) and the Phase-2 `TryClaimForRefundAsync` (customer-cancel, *with* a `CustomerProfileId` owner filter so a customer can only claim their own order). `ReleaseRefundClaimToAsync` reverts `Refunding → toStatus` scoped to the still-`Refunding` row — the parameterised rollback the admin path uses. And because `ExecuteUpdate` **bypasses the `AuditingInterceptor**, every one of these set-based statements **hand-stamps `UpdatedAt`/`UpdatedBy`** — the same lesson Phase 2 hammered.

#### `Mappers/AdminOrderMappers.cs` + the admin DTOs

Explicit mapping (no AutoMapper, per CODING_STANDARDS). The reason a *separate* admin mapper exists rather than reusing `OrderMappers`: the admin view **surfaces what the customer view deliberately omits** — the buyer's email, the full payment ledger, and the shipment. The email resolution is the mapper's one piece of logic, and it mirrors the repository's two-source identity:

```csharp
private static string CustomerEmailOf(Order order) =>
    order.GuestEmail ?? order.CustomerProfile?.User?.Email ?? "(unknown)";
```

Guest email first, then the member's Identity email, then a defensive `"(unknown)"` (it never returns null — the DTO's `CustomerEmail` is non-nullable). The detail mapper sorts lines by name and **payments by `CreatedAt`** so the ledger reads charge-then-refund in time order, and projects the optional `Shipment?` through `ToDto()` (null on an unshipped order).

The DTOs themselves are flat `record`s and every status is mapped **`.ToString()`** — the enum is sent as its *name* (`"Paid"`, `"Shipped"`), not its numeric value, so the SPA renders human labels and isn't coupled to the byte mapping. `AdminOrderSummaryDto` adds `CustomerEmail` + nullable `ShipmentStatus` to the customer summary; `AdminOrderDetailDto` adds `CustomerEmail`, the `Payments` ledger, and the `Shipment?`. `PaymentDto.AmountCents` is signed (positive charge / negative refund) — the ledger is honest about direction.

#### `DTOs/Requests/AdminOrderListQuery.cs` + `MarkShippedRequest.cs` + validator

`AdminOrderListQuery` is the `[FromQuery]` filter bag — `Status` (string, parsed by `ParseStatus`), `From`/`To` (documented inclusive-lower / exclusive-upper, matching the repo), `CustomerEmail` (substring, guest-OR-member), and `Page`/`PageSize` defaulting to `1`/`20`. The honest detail: `HasAnomaly` is a **declared-but-no-op placeholder for the Phase-5 anomaly flag** — accepted now so the contract is stable when the feature lands, rather than a breaking query-shape change later.

`MarkShippedRequest(string Carrier, string TrackingNumber)` is the only write body in the chunk, and `MarkShippedRequestValidator` is deliberately small — `NotEmpty().MaximumLength(60)` on carrier, `≤ 120` on tracking. Caps that match the column lengths so a 422 fires at the edge rather than a `DbUpdateException` at the DB.

### Chunk 2 — what to know cold

1. **Three capability policies, not blanket-admin** — `Orders.View` + `Orders.Fulfill` = Staff+StoreManager+Administrator; `Orders.Refund` = **StoreManager+Administrator only**. A Staff who can ship gets a 403 on `/refund`. Policies defined once in `Program.cs`, applied as `[Authorize(Policy = …)]`.
2. **Per-action concurrency is asymmetric** — **ship** (Paid→Fulfilled + create Shipment) and **refund** (the claim) are guarded by `Order.RowVersion` → 409 on a stale write; **deliver** is an idempotent shipment update, benign last-write-wins, no token.
3. **`MarkShipped` is one tracked `SaveChanges`** (flip status + insert Shipment atomically); the interceptor auto-audits the diffs and the explicit `_audit.Record("Shipped", …)` adds the legible business event on top. `OrderStatus` only goes to `Fulfilled`; the finer Pending→Shipped→Delivered lives on `ShipmentStatus` (never renumber the serialized `OrderStatus`).
4. **Admin refund is Paid-ONLY (a Fulfilled/shipped order is a deferred RMA) and accepts `Refunding` as a re-drivable RECOVERY state.** `TryClaimForRefundByIdAsync` is a set-based `Where(Status==Paid).ExecuteUpdate(→Refunding)` whose `affected == 1` is the **exactly-one-winner TOCTOU guard** *before* Stripe is touched; Stripe failure → `ReleaseRefundClaimToAsync(→Paid)` + flush.
5. **The named "Refund" + per-variant "Restocked" rows are recorded once, inside `OrderRefundService`, at the real transition, with the TRUE prior status** — so a recovery re-drive or a concurrent `charge.refunded` webhook can't duplicate them; `Refunded` early-return + `Order.RowVersion` together make the reversal idempotent and double-restock-proof.
6. **`ParseStatus` = `Enum.TryParse(...) && Enum.IsDefined(...)`** — `IsDefined` is mandatory because `TryParse` accepts in-range garbage like `"99"` on a `byte` enum and would silently return an always-empty page.
7. **The customer-email filter joins two identity sources** — `GuestEmail` (guest) OR `CustomerProfile.User.Email` (member), the Chunk-0 guest-XOR-member invariant resurfacing at the read layer; reads are `AsNoTracking` and **not owner-scoped** (staff see all), while ship/deliver use the one tracked `GetTrackedWithShipmentAsync`. Every set-based `ExecuteUpdate` hand-stamps `UpdatedAt`/`UpdatedBy` because it bypasses the interceptor.
8. **`DateRangeGuard.Validate` → 422 on a reversed range** — a shared guard (audit/orders/report) that turns a silent always-false-predicate 200 into an actionable error; statuses go over the wire as `.ToString()` names, money stays signed integer cents.

---

## 5. Chunk 3 — Audit viewer, sales report, inventory adjust

Three small, self-contained read/write features that all lean on machinery built in earlier phases. The **audit viewer** finally surfaces the trail the interceptor has been silently stamping since Phase 0; the **sales report** is the project's first aggregate query; and the **inventory adjust** closes a Phase-2 gap — admin stock changes that *weren't* being audited. All three are gated by the new Phase-3 `*.View` / `Inventory.Adjust` policies (Chunk 2's RBAC matrix), and two of the three share one tiny cross-field validator, `DateRangeGuard`.

### What is in Chunk 3

```
src/api/Retail.Api/
├─ Controllers/AuditLogsController.cs        ← GET /api/v1/audit-logs   [Audit.View]
├─ Services/IAuditQueryService.cs / AuditQueryService.cs   ← read model OVER the AuditLog ledger (injects DbContext)
├─ DTOs/Requests/AuditLogListQuery.cs        ← optional actor/entity/date filters + paging
├─ DTOs/Responses/AuditLogDto.cs             ← one trail row (incl. Before/After JSON)
│
├─ Controllers/AnalyticsController.cs        ← GET /api/v1/analytics/sales-by-day   [Reports.View]
├─ Services/IReportQueryService.cs / ReportQueryService.cs   ← load paid orders → aggregate IN MEMORY
├─ DTOs/Requests/SalesByDayQuery.cs          ← optional [from, to); defaults last 30 days
├─ DTOs/Responses/SalesReportDto.cs          ← Days[] (the chart) + Categories[] (the breakdown)
│
├─ Controllers/AdminInventoryController.cs   ← POST /api/v1/admin/inventory/{variantId}/adjust   [Inventory.Adjust]
├─ Services/IAdminInventoryService.cs / AdminInventoryService.cs   ← TRACKED load + OnHand += delta + named audit row
├─ DTOs/Requests/AdjustInventoryRequest.cs   ← (int Delta, string Reason)
├─ Validators/AdjustInventoryRequestValidator.cs   ← non-zero delta, reason ≤ 200
├─ DTOs/Responses/StockDto.cs                ← OnHand / Reserved / Available after the adjust
└─ Common/Validation/DateRangeGuard.cs       ← shared reversed-range → 422 (+ optional span cap)
```

### Per-file purpose — the audit viewer

#### `Services/AuditQueryService.cs` (resume-gold)

This is the interesting one: it is a **read model that reads the ledger directly**, and it deliberately breaks the project's own repository convention to do so. It injects `RetailDbContext`, not a repository:

```csharp
public AuditQueryService(IAuditQueryService /*…*/) // ctor takes RetailDbContext _db
// …
IQueryable<AuditLog> rows = _db.AuditLogs.AsNoTracking();
```

The class comment states the rationale outright: *"the audit log is a technical, append-only read model (no repository / domain rules), so the query lives here."* An audit row has no lifecycle, no invariants, no business operations — there is nothing for a domain layer to protect. Wrapping it in a repository would be ceremony with zero payoff, so the service *is* the data access. (Contrast the Order aggregate, which earns its repository because it has rules.)

Every filter is **exact-match on an indexed column**, and the indexes are the whole reason the filters are shaped this way:

```csharp
rows = rows.Where(a => a.Actor == actor);           // IX_AuditLog_Actor_OccurredAt
rows = rows.Where(a => a.EntityType == entityType); // IX_AuditLog_EntityType_EntityId
rows = rows.Where(a => a.EntityId == entityId);     //   "
rows = rows.Where(a => a.OccurredAt >= from);       // IX_AuditLog_OccurredAt
rows = rows.Where(a => a.OccurredAt < to);
```

There are exactly three `IX_AuditLog_*` indexes (`Actor+OccurredAt`, `EntityType+EntityId`, `OccurredAt`), and the three search modes the UI offers — *"what did this actor do,"* *"what happened to this record,"* *"what happened in this window"* — map one-to-one onto them. The query is **exact `==`, never `LIKE`/`Contains`**, because a leading-wildcard `LIKE` is non-sargable (can't use the index) and the trail is too high-volume to scan. The order is `OrderByDescending(OccurredAt).ThenByDescending(Id)` — newest-first, with `Id` as a **stable tiebreak** so two rows sharing a timestamp don't shuffle between pages (the classic unstable-paging bug). Paging is defensively clamped in the service itself (`safeSize = Math.Clamp(query.PageSize, 1, 100)`), so a hostile `pageSize=100000` can't turn one request into a full-table dump.

**Interview note — the `[from, to)` half-open range.** `From` is `>=` (inclusive) and `To` is `<` (exclusive). Half-open ranges compose without double-counting: yesterday's `To` equals today's `From`, and the boundary instant belongs to exactly one of them. This is the *same* convention the sales report uses, which is why it lives in one shared guard.

#### `DTOs/Requests/AuditLogListQuery.cs` + `DTOs/Responses/AuditLogDto.cs`

The query is a `record` of **all-optional** filters (`Actor`, `EntityType`, `EntityId`, `From`, `To`) plus `Page`/`PageSize` defaulting to `1`/`20` — a bare `GET /api/v1/audit-logs` returns the newest 20 rows of everything. The DTO is a near-flat projection of the entity, and it deliberately **carries `BeforeJson` and `AfterJson` through to the client**:

```csharp
public sealed record AuditLogDto(
    long Id, string Actor, string Action, string EntityType, string EntityId,
    string? BeforeJson, string? AfterJson, DateTimeOffset OccurredAt);
```

Surfacing the before/after diff is the whole *point* of an audit viewer — "who changed this order's status from `Paid` to `Refunded`, and what did the row look like on each side." It's safe to expose because the JSON was **PII-redacted at write time** by the interceptor/`AuditWriter` (the DTO trusts that upstream contract rather than re-sanitizing).

#### `Controllers/AuditLogsController.cs` — and why export is deferred

A thin boundary: validate the date range, call the service, wrap in the envelope.

```csharp
[Authorize(Policy = Roles.Policies.AuditView)]
public async Task<IActionResult> Search([FromQuery] AuditLogListQuery query, CancellationToken ct)
{
    if (DateRangeGuard.Validate(query.From, query.To) is { } invalid)
        return UnprocessableEntity(invalid);
    PagedResult<AuditLogDto> result = await _audit.SearchAsync(query, ct);
    return Ok(ApiResponse<PagedResult<AuditLogDto>>.Ok(result));
}
```

Note it calls `DateRangeGuard.Validate(...)` **with no `maxSpanDays`** — the audit search is index-backed, so an arbitrarily wide window is cheap; it only needs the reversed-range check. (The report, which aggregates in memory, *does* pass a cap — same guard, different argument.)

The class comment names a product decision worth knowing cold: *"view-only (export is deferred, which is what keeps Staff read-only)."* The RBAC matrix has a **separate `Audit.Export` policy** (and a `Reports.Export`) that no endpoint consumes yet. That separation is the lever: `Staff` holds `Audit.View` but not `Audit.Export`, so until an export endpoint ships, **Staff literally cannot exfiltrate the audit trail in bulk** — they can only page through it in the viewer. Deferring export isn't laziness; it's how the least-privilege boundary is kept tight.

### Per-file purpose — the sales report

#### `Services/ReportQueryService.cs` (resume-gold)

The project's first aggregate query — and it computes the aggregate **in application memory, not in SQL**, a choice the file is loud and honest about. It loads the paid orders in range with their full line→category graph, then `GroupBy`s in LINQ-to-objects:

```csharp
private static readonly OrderStatus[] PaidStatuses = { OrderStatus.Paid, OrderStatus.Fulfilled };
// …
List<Order> orders = await _db.Orders.AsNoTracking()
    .Where(o => PaidStatuses.Contains(o.Status) && o.PlacedAt >= from && o.PlacedAt < to)
    .Include(o => o.Lines)
        .ThenInclude(line => line.ProductVariant!)
        .ThenInclude(variant => variant.Product!)
        .ThenInclude(product => product.Category)
    .ToListAsync(ct);
```

Three decisions stack up here:

- **"Paid" means `{Paid, Fulfilled}`, not just `Paid`.** Once an order ships it transitions `Paid → Fulfilled` (the Phase-3 workbench), but the *revenue already happened*. Counting only `Paid` would make a day's sales **drop the instant you marked an order shipped** — a nonsense report. `Pending`/`Cancelled`/`Refunded` are correctly excluded (no captured revenue). This is why the predicate is a set membership, and it's the kind of off-by-one-status bug that's invisible until someone ships an order mid-month.
- **Aggregate in memory — on purpose, with a documented exit.** The comment: *"at portfolio scale this is trivially fast and avoids an EF date-grouping translation; a SQL `GROUP BY` / indexed report view is the Phase-10 optimisation if k6 ever shows a hot path."* Grouping by `DateOnly` and a multi-level category nav doesn't translate cleanly to a single SQL `GROUP BY`, so rather than fight the provider for a dataset that's a few hundred rows, it pulls the orders and groups with LINQ-to-objects. The honesty is the resume gold: it's a **conscious, bounded, reversible** trade-off (and the `DateRangeGuard` 366-day cap is what *makes* it bounded), not an accident.
- **The range is `[from, to)` against `PlacedAt`**, the same half-open convention as the audit viewer.

The day grouping keys on a UTC `DateOnly` and emits an ISO string the client can chart directly:

```csharp
.GroupBy(o => DateOnly.FromDateTime(o.PlacedAt.UtcDateTime))
.OrderBy(group => group.Key)
.Select(group => new DailySalesDto(
    group.Key.ToString("yyyy-MM-dd"),
    group.Count(),
    group.Sum(o => (long)o.TotalCents)))
```

The `(long)` cast on the `Sum` is the same overflow hygiene as checkout: a busy day's cents can exceed `int.MaxValue`, so the accumulator must be `long`. Days with **zero** orders simply don't appear — gap-filling the calendar is left to the chart.

The category breakdown is the subtle part, and the `"(uncategorised)"` fallback is load-bearing:

```csharp
List<CategorySalesDto> categories = orders
    .SelectMany(o => o.Lines)
    .GroupBy(line => line.ProductVariant?.Product?.Category?.Name ?? "(uncategorised)")
    .Select(group => new CategorySalesDto(group.Key, group.Sum(line => (long)line.LineTotalCents)))
    .OrderByDescending(category => category.TotalSalesCents)
    .ToList();
```

It groups by **line totals**, not order totals — so it's *merchandise* revenue per category (it deliberately excludes tax/shipping, which belong to no category). The `?.…?? "(uncategorised)"` null-coalescing chain handles the case the comment calls out: a product that was **soft-deleted since the sale** comes back `null` through the global query filter, so its historical revenue would otherwise vanish from the breakdown — and because the report sums to a total, a silently-dropped category would make the numbers *not add up*. Bucketing the orphans into `"(uncategorised)"` keeps the breakdown reconciled to the daily totals. This is the snapshot pattern's shadow: the `OrderLine` froze its money, but the *category nav still points at live (and deletable) catalog*, so the report must tolerate a dangling reference.

#### `Controllers/AnalyticsController.cs` (resume-gold) — defaulting + the guard

The controller owns the **defaulting**, and the order of operations is the thing to know cold:

```csharp
DateTimeOffset to = query.To ?? _timeProvider.GetUtcNow();
DateTimeOffset from = query.From ?? to.AddDays(-30);

// Validate the EFFECTIVE range (after defaulting), so an only-`from` request can't widen the
// window to "now". Reject a reversed range, and cap the span to bound the in-memory aggregation.
if (DateRangeGuard.Validate(from, to, maxSpanDays: 366) is { } invalid)
    return UnprocessableEntity(invalid);
```

- **`to` defaults before `from`** so `from` can default *relative to* `to` (last 30 days of whatever window you asked for), giving a sensible empty-query response.
- **"Now" comes from the injected `TimeProvider`, not `DateTimeOffset.UtcNow`.** Same lesson as the cart sweeper: a test can pin the clock and assert the default-30-day window deterministically. A hard-coded `UtcNow` would make the report's default range untestable.
- **The guard validates the *effective* range, after defaulting** — the comment's subtle point. If you only pass `from`, `to` has already become "now," so the cap and the reversed check apply to the *real* window the query will scan. Validating the raw nullable query *before* defaulting would let an only-`from` request silently widen to an unbounded span, defeating the cap.
- **`maxSpanDays: 366`** is the bound that keeps the in-memory aggregation safe — at most a year of paid orders is ever materialized. This is exactly the argument the audit controller *omits*.

#### `DTOs/Responses/SalesReportDto.cs` + `SalesByDayQuery.cs`

One response, two shapes for two UI elements: `Days[]` (`DailySalesDto(string Date, int OrderCount, long TotalSalesCents)`) drives the time-series chart; `Categories[]` (`CategorySalesDto(string Category, long TotalSalesCents)`) drives the breakdown table. Money stays **integer cents** on the wire — the React client formats it, the API never sends a pre-formatted string or a lossy `decimal`. The query record is just the optional `[from, to)` pair.

### Per-file purpose — the inventory adjust

#### `Services/AdminInventoryService.cs` (resume-gold)

This is the file that **closes a real Phase-2 gap**, and the gap is the headline. In Phase 2, every stock movement (reserve / commit / restock) went through set-based `ExecuteUpdate`, which **bypasses the `AuditTrailInterceptor`** — so those flows were never auto-audited (a deliberate trade for the concurrency primitive). But that left a hole: a *manual admin stock correction* also had no audit story. Chunk 3 fixes it by choosing the **opposite** EF strategy on purpose — a **tracked** load + mutate + `SaveChanges`:

```csharp
InventoryItem item = await _db.InventoryItems.FirstOrDefaultAsync(i => i.ProductVariantId == variantId, ct)
    ?? throw new NotFoundException($"No inventory found for variant '{variantId}'.");

int before = item.OnHand;
int after = before + request.Delta;
// … guard …
item.OnHand = after;
_audit.Record(
    "InventoryAdjusted", nameof(InventoryItem), item.Id.ToString(),
    before: new { OnHand = before },
    after: new { OnHand = after, request.Delta, request.Reason });

await _db.SaveChangesAsync(ct);
```

Why this shape earns its keep:

- **Tracked load → the interceptor fires.** Because `item` is a tracked entity and `OnHand` is mutated in place, the one `SaveChanges` produces an automatic `Update` audit row (before/after JSON) for free — and, because `InventoryItem` is rowversioned, that same write gets **`InventoryItem.RowVersion` optimistic concurrency** for free too: two admins adjusting the same SKU concurrently → one's `SaveChanges` matches 0 rows → `DbUpdateConcurrencyException` → 409. The comment names exactly this: *"a TRACKED load + mutate + SaveChanges, so the AuditTrailInterceptor auto-records the InventoryItem change AND `InventoryItem.RowVersion` makes concurrent adjustments safe."* The Phase-2 `ExecuteUpdate` path got neither for free; this path gets both.
- **A second, *named* audit row on top.** The interceptor's auto-row says "Update, OnHand 40→50"; the explicit `_audit.Record("InventoryAdjusted", …)` row says "a human adjusted by +10 because *'damaged stock writeback'*." The `Reason` (a required field, ≤ 200) is *only* legible in this named row — that's the whole reason `AdjustInventoryRequest` carries a reason. Both rows commit in the **same `SaveChanges`/transaction** (per `AuditWriter`'s contract: it only `Add`s, never saves), so a rolled-back adjustment rolls back its audit trail with it — you can never have an audit row claiming a change that didn't persist.

#### The reserved-aware guard — the part interviewers dig into

The validation that makes this safe is **not** "don't let on-hand go negative." It's "don't let *available* go negative":

```csharp
// Reject anything that would make AVAILABLE (= OnHand − Reserved) negative, not merely
// OnHand < 0. Reserved units are already promised to in-flight checkouts; letting on-hand
// drop below them oversells and breaks the ledger when those reservations commit
// (CommitReservedAsync subtracts unconditionally). With Reserved = 0 this is the on-hand ≥ 0 guard.
if (after < item.Reserved)
{
    throw new ConflictException(
        $"Adjustment would leave on-hand ({after}) below reserved stock ({item.Reserved}), …");
}
```

This is the resume-grade subtlety. `Available = OnHand − Reserved` (a computed, never-stored property on `InventoryItem`). If you only guarded `OnHand >= 0`, an admin could shave on-hand down *below* the units already reserved by in-flight checkouts. When those checkouts pay, Phase-2's `CommitReservedAsync` subtracts from `OnHand` **unconditionally** — so on-hand would go negative *then*, after money has changed hands, with no guard left to catch it. The invariant the system actually needs is `OnHand >= Reserved` (equivalently `Available >= 0`), and `after < item.Reserved` is exactly that, checked **before** the mutation. The comment's last line is the sanity check: when `Reserved == 0`, this collapses to the naive `OnHand >= 0` rule — so the stricter guard is a strict superset, never weaker. A violation is a **409 `ConflictException`** (a business-rule conflict), distinct from the 404 for an unknown variant.

#### `Controllers/AdminInventoryController.cs` + request/validator/`StockDto`

The controller runs FluentValidation up front (`Delta != 0`, `Reason` non-empty ≤ 200 → 422), then delegates. The route is `POST {variantId:guid}/adjust` — **keyed by the variant id, not the inventory-item id**, because the variant is what an admin actually knows (the `InventoryItem` id is an internal surrogate the UI never sees). `StockDto(ProductVariantId, OnHand, Reserved, Available)` echoes the full post-adjust stock state so the admin UI re-renders all three numbers from one round-trip — and surfacing `Reserved`/`Available` (not just the on-hand they changed) is what makes the reserved-aware guard *legible* to the operator who just hit it.

### `Common/Validation/DateRangeGuard.cs` — the shared seam

One static guard, reused by the audit viewer (§7), the order workbench (§8), and this report (§9). It exists because of a specific **silent-failure trap** the comment spells out:

```csharp
// A reversed range (from > to) yields an always-false predicate that reads as "no data"
// with a 200 — a silent client error — so callers reject it up front.
public static ApiResponse? Validate(DateTimeOffset? from, DateTimeOffset? to, int maxSpanDays = 0)
{
    if (from.HasValue && to.HasValue)
    {
        if (from.Value > to.Value)
            return Fail("'from' must be on or before 'to'.", "from");
        if (maxSpanDays > 0 && (to.Value - from.Value).TotalDays > maxSpanDays)
            return Fail($"Date range cannot exceed {maxSpanDays} days.", "to");
    }
    return null;
}
```

Without the guard, a reversed `from > to` produces `WHERE OccurredAt >= from AND OccurredAt < to` that **no row can satisfy** — so the user gets a cheerful empty `200`, indistinguishable from "genuinely no activity." That's a confusing client bug, not a server error, so it's caught up front as a **422** (it returns `null` to mean "valid, proceed," or an `ApiResponse` to mean "reject"). The `maxSpanDays` parameter is **opt-in** (`0` = no cap): the audit viewer passes nothing (index-backed, any width is fine), the report passes `366` (in-memory aggregation must stay bounded). Both bounds must be present for either check to fire — each is independently optional, which is what lets the same guard serve "only-from", "only-to", "both", and "neither" callers without special-casing.

```
audit viewer  ─┐
order workbench ┼─→ DateRangeGuard.Validate(from, to[, maxSpanDays])
sales report  ─┘        reversed (from > to)?  → 422 "'from' must be on or before 'to'"
                        span > cap (report only)? → 422 "cannot exceed 366 days"
                        else null → proceed
```

### Chunk 3 — what to know cold

1. **The audit viewer is a read model straight over the `AuditLog` ledger** — `AuditQueryService` injects `RetailDbContext` (no repository: a technical append-only log has no domain rules), filters **exact-match** on the three `IX_AuditLog_*` indexes (actor / entity / time), and pages newest-first with `Id` as a stable tiebreak.
2. **Export is a *separate* policy (`Audit.Export` / `Reports.Export`) that no endpoint consumes yet** — that's the deliberate lever keeping `Staff` read-only: they can `*.View` in the UI but can't bulk-exfiltrate.
3. **The sales report counts paid orders as `{Paid, Fulfilled}`** (revenue already happened; counting only `Paid` would make sales drop the moment you ship), over a half-open `[from, to)` on `PlacedAt`.
4. **It aggregates in memory by `DateOnly` + per-category, on purpose** — documented as fine at portfolio scale, with a SQL `GROUP BY` as the Phase-10 escape hatch; the `366`-day `DateRangeGuard` cap is what bounds it. Category falls back to `"(uncategorised)"` for since-soft-deleted products so the breakdown still reconciles.
5. **Defaults come from the injected `TimeProvider`** (last 30 days), and the guard validates the **effective** range *after* defaulting, so an only-`from` request can't widen past the cap.
6. **Inventory adjust is a TRACKED load + `OnHand += delta` + `SaveChanges`** — deliberately the opposite of Phase-2's `ExecuteUpdate` so it gets auto-audited (interceptor) *and* `RowVersion` concurrency (→409) for free; a second **named `"InventoryAdjusted"` row** (with delta + reason) commits in the same transaction. This closes the Phase-2 gap that admin stock changes weren't audited.
7. **The adjust guard rejects `Available < 0`, not `OnHand < 0`** (`after < item.Reserved` → 409) — because reserved units are promised to in-flight checkouts and `CommitReservedAsync` later subtracts unconditionally; guarding only on-hand would oversell and push on-hand negative *after* payment.
8. **`DateRangeGuard` exists because a reversed range silently returns an empty 200** — it converts that into an explicit 422, and its opt-in `maxSpanDays` is what the report (but not the audit viewer) uses to stay bounded.

---

## 6. The frontend — AdminShell, the capability mirror, the admin pages

Phase 2's frontend was the *shopper's* surface — a cart, a checkout redirect, an order history. Phase 3 builds the **staff** surface that sits beside it: a separate `/admin` area with its own chrome, gated by a 3-role RBAC matrix that the React side **mirrors but does not own**. The defining theme is **dual enforcement** — every gate you see in the UI exists *only for UX* (to hide buttons and routes a user can't use), and the **server independently re-checks every request**. The frontend's job is to keep that mirror honest and in one place, so the sidebar, the route guards, and the per-button gating never drift from the backend policy matrix.

### What is in the admin frontend

```
src/web/src/
├─ lib/auth/roleSets.ts                       ← ROLE_SETS: the capability map MIRRORING the backend policy matrix (UX-only)
├─ app/router.tsx                             ← the two sibling shells; nested RoleGuard-gated /admin routes
├─ components/layouts/AdminShell.tsx          ← the back-office layout (own sidebar + topbar), sibling of StorefrontShell
├─ components/ui/                             ← the new "compose, not invent" primitives
│  ├─ data-table.tsx                          ← thin generic <table> (scope="col" + aria-label)
│  ├─ dialog.tsx                              ← Modal over Radix Dialog (focus trap, ESC, ARIA)
│  └─ empty-state.tsx                         ← dashed-border placeholder for empty lists
├─ features/admin/
│  ├─ components/SidebarNav.tsx               ← role-gated NavLinks driven by ROLE_SETS
│  ├─ components/VariantsSection.tsx          ← + the inventory-adjust modal (reason ≤200 mirrors the server)
│  ├─ UsersPage.tsx                           ← DataTable + create Modal (StoreManager option hidden for non-admin)
│  ├─ AdminOrdersPage.tsx                     ← the order workbench list (status/email filters)
│  ├─ AdminOrderDetailPage.tsx               ← detail + Mark-Shipped Modal + the gated RefundModal
│  ├─ AuditLogPage.tsx                        ← DataTable + filters + the before/after JSON Modal
│  ├─ ReportsPage.tsx                         ← Recharts line chart + by-category + EmptyState
│  └─ hooks/                                  ← useAdminOrders / useAdminUsers / useAuditLogs / useSalesReport / useAdjustInventory
└─ lib/api/types.ts                           ← Admin* aliases over the generated OpenAPI schema
```

### Per-file purpose

#### `lib/auth/roleSets.ts` (resume-gold) — the capability mirror

This single file is the frontend's whole RBAC model, and the header comment states its constitutional limit up front:

```ts
// Mirrors the backend capability matrix (Roles.Policies.* — PHASE_3_SCOPE.md §3.1). Frontend auth
// is UX-only — the server re-checks every request — but keeping the role→capability mapping in ONE
// place keeps the route guards, the sidebar, and per-element gating consistent and one-edit-to-change
// (no role strings scattered across components).
```

Two exports carry the model. `ADMIN_AREA_ROLES` is the coarse gate — *any* back-office role admits you to `/admin` at all:

```ts
export const ADMIN_AREA_ROLES: string[] = ['Staff', 'StoreManager', 'Administrator']
```

`ROLE_SETS` is the fine-grained matrix — one entry per capability area, each holding the exact role list the backend policy allows:

```ts
export const ROLE_SETS = {
  orders: ['Staff', 'StoreManager', 'Administrator'],
  inventory: ['Staff', 'StoreManager', 'Administrator'],
  audit: ['Staff', 'StoreManager', 'Administrator'],
  reports: ['Staff', 'StoreManager', 'Administrator'],
  users: ['StoreManager', 'Administrator'],
  catalog: ['Administrator'],
  // Capability (not a sidebar area): who may issue a refund — mirrors Orders.Refund.
  refund: ['StoreManager', 'Administrator'],
} satisfies Record<string, string[]>
```

Several design choices here are interview-grade:

- **Why a single map and not inline role checks?** The comment names the failure mode it prevents: "no role strings scattered across components." If `users` were gated by a literal `['StoreManager', 'Administrator']` written into three places (the route guard, the sidebar item, the page), changing the policy means finding all three — and missing one is a silent authz drift. One map means **one edit changes every consumer at once**, and the three consumers (router, `SidebarNav`, per-element gating) are guaranteed consistent by construction.
- **`refund` is a capability, not an area.** Notice `refund` has no sidebar item and no route — it gates a single *button* inside `AdminOrderDetailPage`. Modelling it in the same map as the areas means the "who may refund" rule lives next to "who may see orders," even though one is a page and the other a button. The comment ties it to its backend twin: "mirrors `Orders.Refund`."
- **`satisfies Record<string, string[]>`, not `: Record<...>`.** Using `satisfies` keeps the *literal* type — so `keyof typeof ROLE_SETS` is the precise union `'orders' | 'inventory' | … | 'refund'`, exported as `AdminArea`. An annotation (`: Record<string, string[]>`) would widen the key type to `string` and lose that. This is what lets `SidebarNav`'s `area?: AdminArea` field be type-checked against real keys — a typo'd area name is a **compile error**, not a silently-always-hidden nav item.
- **`hasAnyRole` is the one predicate everything routes through:**
  ```ts
  export function hasAnyRole(
    userRoles: readonly string[] | undefined,
    allowed: readonly string[],
  ): boolean {
    return userRoles?.some((role) => allowed.includes(role)) ?? false
  }
  ```
  It's null-safe (`?? false`) so an unauthenticated user with `undefined` roles cleanly fails closed, and the doc-comment repeats the load-bearing caveat: "UX gating only — the backend is the gate." **Interview gotcha:** the role names are `Administrator` / `StoreManager` / `Staff` / `Customer` — *not* `"Admin"`. They must match the seeded backend role names exactly, because these strings are compared against the roles claim the server put in the user object; a casual `"Admin"` would gate nothing correctly.

#### `components/layouts/AdminShell.tsx` — the sibling layout

`AdminShell` is the back-office's own top-level chrome — deliberately **not** nested inside the storefront. The header comment is explicit about why:

```tsx
/**
 * Admin back-office layout: a role-driven sidebar + a topbar + routed content. Distinct from the
 * storefront's <StorefrontShell /> — the /admin area is its own surface (PHASE_3_SCOPE.md §12), so
 * it does NOT render the storefront header.
 */
```

The structure is a `<aside>` (logo + `<SidebarNav />`) beside a `<main>` holding the React Router `<Outlet />`, with a thin topbar carrying a "← Storefront" link, the signed-in email, and Sign out. The design rationale:

- **It's a sibling shell, not a child route.** In the router (below), `AdminShell` and `StorefrontShell` are two separate top-level entries. If `/admin` rendered *inside* `StorefrontShell`, every admin page would carry the customer header/cart-badge — wrong audience, wrong chrome. Making it a sibling gives the back-office a clean surface that shares only the design system, not the layout.
- **Sign-out reuses the storefront's `useSessionActions`.** The admin shell doesn't reinvent logout — it calls the same `signOut()` that the storefront uses, which (from Phase 2's C2 fix) `removeQueries(['cart'])` *and* `removeQueries(orderKeys.all)`. So an admin signing out gets the exact same no-stale-window cache clear, then is navigated to `/` (the storefront), not a login page:
  ```tsx
  async function handleSignOut() {
    await apiClient.POST('/api/v1/auth/logout')
    signOut()
    navigate('/')
  }
  ```
- **It renders no authz of its own.** `AdminShell` assumes it only ever mounts behind a `RoleGuard` (the router enforces that). It reads `user` purely to *display* the email — the access decision lives in the route, not the layout. That separation keeps the shell dumb and the guard the single enforcement point.

#### `features/admin/components/SidebarNav.tsx` — role-gated navigation

The sidebar is a flat `ITEMS` list where each entry optionally names a capability area, and the component filters the list by `hasAnyRole` before rendering:

```tsx
const ITEMS: NavItem[] = [
  { to: '/admin', label: 'Dashboard' },
  { to: '/admin/orders', label: 'Orders', area: 'orders' },
  { to: '/admin/products', label: 'Products', area: 'catalog' },
  { to: '/admin/inventory', label: 'Inventory', area: 'inventory' },
  { to: '/admin/audit', label: 'Audit log', area: 'audit' },
  { to: '/admin/reports', label: 'Reports', area: 'reports' },
  { to: '/admin/users', label: 'Users', area: 'users' },
]
// ...
const visible = ITEMS.filter(
  (item) => item.area === undefined || hasAnyRole(roles, ROLE_SETS[item.area]),
)
```

The design points:

- **The sidebar is the visible proof the RBAC matrix is real.** The doc-comment says it directly: this is "what makes Administrator / StoreManager / Staff each see a DIFFERENT sidebar." A `Staff` user (no `users`, no `catalog` role) sees neither "Users" nor "Products"; a `StoreManager` sees "Users" but not "Products"; an `Administrator` sees everything. The whole demo of the phase is "log in as each role, watch the nav change."
- **`area === undefined` means "any admin role".** The Dashboard item has no `area`, so it's visible to anyone who got past the `/admin` gate. This is the one item driven by membership-in-the-area rather than a specific capability.
- **It reads `ROLE_SETS[item.area]` — the same map the router reads.** The sidebar and the route guard consult one source. If the sidebar showed an item the route then blocked (or vice versa), you'd get the classic "I can see the link but clicking 404s/403s" bug. Sharing `ROLE_SETS` makes that impossible.
- **`end={item.to === '/admin'}`** — the Dashboard NavLink is active-styled only at *exactly* `/admin`, not on `/admin/orders`. Without `end`, the root link's prefix match would keep it highlighted everywhere in the area.

#### `app/router.tsx` — two sibling shells, nested RoleGuard gates

The router is where the dual-shell + dual-layer-gate architecture is wired. The top-level array has **two** layout entries; the comment block spells out both intents:

```tsx
//  TWO top-level layouts: the public storefront (<StorefrontShell /> header) and the
//  back-office (<AdminShell /> sidebar). They are SIBLINGS, not nested...
//
//  RBAC (Phase 3): the /admin area is gated to any back-office role; each child route
//  further-gates to its capability via ROLE_SETS (mirroring the backend policies)...
```

The `/admin` branch shows the **two-layer gate** in code — a coarse area guard wrapping the shell, and a fine capability guard wrapping each child:

```tsx
{
  path: '/admin',
  element: (
    <RoleGuard allowedRoles={ADMIN_AREA_ROLES}>
      <AdminShell />
    </RoleGuard>
  ),
  children: [
    { index: true, element: <AdminHomePage /> },
    { path: 'orders', element: (<RoleGuard allowedRoles={ROLE_SETS.orders}><AdminOrdersPage /></RoleGuard>) },
    { path: 'users', element: (<RoleGuard allowedRoles={ROLE_SETS.users}><UsersPage /></RoleGuard>) },
    // ...products/audit/reports/inventory similarly, each with its own ROLE_SETS.* guard
  ],
}
```

Why two layers, not one? The outer `ADMIN_AREA_ROLES` guard is the cheap "are you staff at all" check that protects the shell (so a `Customer` never even sees the admin chrome). The inner per-route guards encode the *capability* matrix — a `Staff` user passes the outer gate and reaches `AdminShell`, but the `users` route's `ROLE_SETS.users` guard (StoreManager + Administrator) turns them away. This mirrors the backend exactly: the API has a blanket "back-office" requirement *and* per-endpoint policies. Catalog routes use `ROLE_SETS.catalog` (`Administrator`-only), and the comment notes React Router ranks the static `products/new` segment above `products/:id` so both resolve. The `inventory` route is a deliberate placeholder (`AdminComingSoon`) that points staff at the per-variant adjust flow inside the product editor — the real inventory write lives in `VariantsSection`, not a dashboard.

#### The new primitives: `data-table.tsx`, `dialog.tsx`, `empty-state.tsx` (resume-gold)

Phase 3 needed tables, modals, and empty placeholders across five admin pages. Rather than pull a heavy grid library, it adds three thin, hand-built primitives — the **"compose, not invent"** philosophy stated verbatim in `DataTable`'s header:

```tsx
/**
 * Thin, hand-built table primitive (PHASE_3_SCOPE.md §3.6 — "compose, not invent"). Columns describe
 * the headers + cell renderers; the PARENT owns data fetching, paging, and filtering. Deliberately
 * minimal (no built-in sort/virtualisation, no heavy dependency) so it stays a reusable building
 * block the admin pages compose rather than each hand-rolling a <table>.
 */
export function DataTable<T>({ columns, rows, getRowKey, empty, label }: DataTableProps<T>) {
```

The interview-relevant design rationale across the three:

- **`DataTable<T>` is generic and inversion-of-control.** A `Column<T>` is `{ key, header, cell: (row: T) => ReactNode, className? }` — the parent supplies *how* to render each cell, so the same table renders users, orders, and audit rows. Crucially the table **owns no state**: no fetching, no paging, no filtering. Each page wires its own `useQuery` + `<Pagination>` and feeds `rows` in. That's why it stays ~60 lines and never grows a config surface. A heavyweight grid would invert this — it would own the data lifecycle and fight the page's TanStack Query cache.
- **Accessibility is baked into the primitive, not each page.** Headers render `<th key={col.key} scope="col">` and the `<table>` carries `aria-label={label}` — so every admin table gets a screen-reader name ("Users", "Orders", "Audit log") and column semantics for free. Putting this in the primitive means no page can forget it.
- **`empty` is a render prop, not a built-in.** `DataTable` shows `empty` only when `rows.length === 0 && empty !== undefined`, so the *page* decides what "no data" looks like (usually an `EmptyState`). This keeps the table dumb about domain copy.
- **`Modal` wraps Radix Dialog precisely so a11y isn't hand-rolled:**
  ```tsx
  /**
   * Accessible modal dialog over Radix Dialog — focus trap, ESC-to-close, scroll lock, and ARIA
   * labelling (title/description) come from Radix so we don't hand-roll a11y. Controlled via
   * `open`/`onOpenChange`.
   */
  ```
  It's a controlled component (`open` + `onOpenChange`), rendering `Dialog.Title`/`Dialog.Description` so the dialog is announced. **Why Radix?** Focus-trapping, `Esc`, scroll-lock, and `aria-labelledby` wiring are exactly the things hand-rolled modals get subtly wrong; delegating them to Radix is the responsible call for an interview-grade a11y story.
- **`EmptyState`** is a centered dashed-border placeholder (`title` + optional `description` + optional `action`) — used both for empty lists and not-yet-built areas (`AdminComingSoon` builds on the same idea).

#### `features/admin/UsersPage.tsx` — DataTable + create Modal, with a role-aware option

`UsersPage` composes the new primitives: a `DataTable<AdminUser>` (email / display name / role-badges columns) with `Pagination`, plus a `CreateUserDialog` (a `Modal` wrapping a form). The load-bearing detail is the **role-aware create option**:

```tsx
const isAdministrator =
  useAuthStore((state) => state.user?.roles.includes('Administrator')) ?? false
// ...
<CreateUserDialog canCreateManager={isAdministrator} />
```

and inside the dialog's role `<Select>`:

```tsx
<option value="Staff">Staff</option>
{canCreateManager ? <option value="StoreManager">Store Manager</option> : null}
```

The reasoning, and the honesty about it:

- **The "Store Manager" option is hidden for a non-administrator** — a `StoreManager` (who *can* reach this page, since `users` admits them) may only create `Staff`, so the elevated option simply isn't offered. The header comment is careful to say this is *both* sides: "the role option is hidden **+** the server enforces it." Hiding the option is pure UX; the real privilege-escalation defense is the API rejecting a `StoreManager → StoreManager` create with a 403.
- **That 403 is surfaced, not swallowed.** `useCreateUser`'s `serverMessage(error)` deliberately pulls the server's envelope message so the toast reads the actual reason — its comment calls out "the 403 a StoreManager gets trying to create a StoreManager." So even if the client-side hide were bypassed, the user sees a meaningful error rather than a generic failure.
- **`useCreateUser` invalidates rather than writes through.** Unlike the order mutations (below), a create returns one new user but the list is paged/filtered, so it `invalidateQueries(['admin','users'])` to refetch the authoritative page. The client password field mirrors the server policy (`minLength={12}`, "a letter and a digit") so the common failure is caught before the round-trip.

#### `features/admin/AdminOrdersPage.tsx` — the workbench list

The order workbench is a filtered, paged `DataTable<AdminOrderSummary>`. Columns are order number (a `<Link>` to the detail route), placed-date, customer email, an `OrderStatusBadge` (reused from the customer side — Phase 2's forward-compatible `?? 'secondary'` badge), a shipment badge, and a right-aligned total. Filter state lives in the page:

```tsx
const { data, isLoading, isError } = useAdminOrdersQuery({
  page, pageSize: PAGE_SIZE,
  status: status || undefined,
  customerEmail: email || undefined,
})
// Changing a filter resets to page 1.
function setFilter(setter: (value: string) => void, value: string) {
  setter(value)
  setPage(1)
}
```

The design notes: the page is the **stateful orchestrator** the dumb `DataTable` is not — it owns `page`/`status`/`email`, derives the query key from them, and renders `Skeleton` / error / `EmptyState` branches itself. `setFilter` resetting to page 1 is the small correctness detail that prevents "filtered to 2 results but stuck on page 3 → empty." Empty-string filters become `undefined` so they're omitted from the query rather than sent as blank. Reusing `OrderStatusBadge` means the admin and customer views can never disagree on what "Refunded" looks like.

#### `features/admin/AdminOrderDetailPage.tsx` (resume-gold) — detail, ship, and the gated refund

This is the richest admin page: the order's customer, line items, money breakdown, payment ledger, shipment panel, and shipping address — plus the **state- and role-gated action bar**. The actions encode the server-side state machine:

```tsx
{status === 'Paid' ? <Button onClick={() => setShipOpen(true)}>Mark shipped</Button> : null}

{status === 'Fulfilled' && shipmentStatus === 'Shipped' ? (
  <Button ... onClick={() => markDelivered.mutate(...)}>Mark delivered</Button>
) : null}

{canRefund && status === 'Paid' ? (
  <Button variant="destructive" onClick={() => setRefundOpen(true)}>Refund</Button>
) : null}
```

and `canRefund` comes straight from the capability mirror:

```tsx
const canRefund = hasAnyRole(roles, ROLE_SETS.refund)
```

The decisions worth knowing:

- **Refund is gated by *both* role and state.** `canRefund && status === 'Paid'` — a `Staff` user (not in `ROLE_SETS.refund`) never sees the button at all, and even a `StoreManager` only sees it on a `Paid` order. This is `ROLE_SETS.refund` doing exactly the job it was designed for: a per-button capability check, not a route or sidebar gate. The server independently enforces `Orders.Refund`, so a hidden button is convenience, not security.
- **The action buttons *are* the state machine.** "Mark shipped" only on `Paid`; "Mark delivered" only on `Fulfilled` + shipment `Shipped`; refund only on `Paid`. The UI never offers a transition the backend would reject — the same `status`-driven rendering Phase 2 used for the customer Cancel button, now covering the staff transitions.
- **The `RefundModal` is accessible and *shows the amount*:**
  ```tsx
  <Modal
    title={`Refund order #${orderNumber}`}
    description={`This refunds the customer ${formatCents(totalCents)} and restocks the items. This can’t be undone.`}>
    // ...
    <Button type="button" variant="destructive" disabled={refund.isPending} onClick={onConfirm}>
      {refund.isPending ? 'Refunding…' : `Refund ${formatCents(totalCents)}`}
    </Button>
  ```
  An irreversible money action gets a confirmation step (Radix's focus-trapped dialog), the **exact dollar amount is stated in both the description and the confirm button label** (formatted from integer cents via `formatCents` — money never leaves cents until the render edge), and the button disables while pending so a double-click can't fire two refunds. The "restocks the items" / "can't be undone" copy matches the server's reversal semantics.
- **The `MarkShippedModal`** is a small form (carrier + tracking, both `required`) that posts to `/ship`; its description says "the order moves to Fulfilled," matching the server transition. On success it toasts, clears the form, and closes.
- **Write-through via `applyOrderUpdate`.** Every order mutation returns the *authoritative updated order*, so the hooks write it straight into the detail cache and only invalidate the list:
  ```ts
  function applyOrderUpdate(queryClient, id, order) {
    queryClient.setQueryData(adminOrderKeys.detail(id), order)
    void queryClient.invalidateQueries({ queryKey: adminOrderKeys.all })
  }
  ```
  This is the Phase-2 write-through pattern carried into the admin side — one round-trip refreshes the detail page (the action bar re-renders against the new `status`, so "Mark shipped" vanishes the moment it succeeds) while the list refetches lazily.

#### `features/admin/AuditLogPage.tsx` — filters + the before/after JSON Modal

The audit viewer is a `DataTable<AuditLog>` over the immutable trail the `AuditingInterceptor` has stamped since Phase 0, with `entityType` / `actor` filters and a per-row "Details" button that opens the diff:

```tsx
const [detail, setDetail] = useState<AuditLog | null>(null)
// ...row "Details" button:  onClick={() => setDetail(row)}
// ...the modal body:
<JsonBlock label="Before" json={detail.beforeJson} />
<JsonBlock label="After" json={detail.afterJson} />
```

and the safe rendering:

```tsx
function JsonBlock({ label, json }) {
  return (
    <div>
      <p className="mb-1 font-medium text-muted-foreground">{label}</p>
      <pre className="max-h-48 overflow-auto rounded bg-muted p-2">
        {json ? prettyJson(json) : '—'}
      </pre>
    </div>
  )
}
function prettyJson(value: string): string {
  try { return JSON.stringify(JSON.parse(value), null, 2) }
  catch { return value }
}
```

The interview-relevant points:

- **The before/after JSON is rendered into a `<pre>` as text, never as HTML.** The audit payload is arbitrary stored data (could contain `<script>`), so it goes through `prettyJson` (parse → re-stringify with indentation) and lands as a **text child of `<pre>`** — React escapes it. There is no `dangerouslySetInnerHTML` anywhere near it. This is the correct way to display untrusted stored content.
- **`prettyJson` fails soft.** If a row's JSON doesn't parse (legacy/odd data), it falls back to the raw string rather than throwing and blanking the modal. The viewer never crashes on bad data.
- **It's view-only by design.** No edit/delete — the audit trail is immutable, so the page offers only search and inspect. `entityType` defaults like "Order" guide the filter; `shortId` truncates long entity ids in the table so the row stays scannable, with the full diff one click away. `getRowKey={(row) => String(row.id)}` because audit ids are numeric (the bigint-identity pattern from Phase 2's ledger family).

#### `features/admin/ReportsPage.tsx` — Recharts line chart + by-category + EmptyState

The sales report renders a Recharts `LineChart` of paid-order revenue per day plus a category breakdown, with an `EmptyState` for the no-data case:

```tsx
// Plot dollars (cents / 100); the data carries integer cents.
const days = (data.days ?? []).map((d) => ({
  date: d.date,
  total: (d.totalSalesCents ?? 0) / 100,
}))
// ...
<YAxis ... tickFormatter={(value) => `$${value}`} />
<Tooltip formatter={(value) => `$${Number(value).toFixed(2)}`} />
```

Design notes:

- **Cents-to-dollars conversion happens at the chart boundary, on purpose.** The whole stack carries integer cents (the money invariant), but Recharts plots numbers and axis ticks expect dollars — so `totalSalesCents / 100` is applied *only* when building the chart's data array, with the comment flagging it. The category list below it keeps using `formatCents` directly (no division), because it formats for display rather than plotting.
- **The tooltip is currency-formatted, not raw.** `formatter={(value) => `$${Number(value).toFixed(2)}`}` and the `YAxis` `tickFormatter` both prefix `$` and fix two decimals, so a hovered point reads "$42.50" rather than "42.5" — the small touch that makes a money chart look intentional.
- **`EmptyState` carries an honest forward-reference.** When `days.length === 0` it explains *why* it's empty and where data will come from: "Sales appear here once orders are placed. (Rich sample data lands with the Phase-5 synthetic-order seeder.)" — turning a bare empty chart into a self-documenting placeholder. A nested guard also shows "No category data" if days exist but categories don't.

#### `features/admin/components/VariantsSection.tsx` — the inventory-adjust modal

The real inventory write in Phase 3 lives here, not on a dashboard: each active variant row has an "Adjust" button opening `AdjustStockModal`, a signed-delta + reason form. Its **client validation deliberately mirrors the server validator**:

```tsx
const valid =
  delta.trim() !== '' &&
  Number.isInteger(parsedDelta) &&
  parsedDelta !== 0 &&
  trimmedReason.length > 0 &&
  trimmedReason.length <= 200
```

with the rationale in the component doc:

```tsx
/**
 * Stock-adjustment modal: a signed delta + a reason, posted to the inventory-adjust endpoint.
 * Client validation mirrors the server validator (delta a non-zero integer; reason non-empty and
 * ≤ 200 chars) so an over-long reason is caught before the request instead of bouncing as a 422.
 * The reason is recorded in the audit log.
 */
```

Why this matters: the **reason ≤200 and delta-non-zero-integer rules are duplicated on the client to match the server**, so the common mistakes (blank reason, 201-char reason, delta of 0) are caught instantly instead of round-tripping to a 422 — the same "mirror the server's constraint at the edge for UX, but the server is still the authority" pattern as the password rules in `UsersPage`. The `<Input>` even carries `maxLength={200}` as a second guard. The audit linkage is the point of forcing a reason at all: `useAdjustInventory` posts `{ delta, reason }`, the server stamps that reason into the audit trail, and the same `AuditLogPage` above can later show *why* on-hand changed. On success the toast echoes the authoritative new on-hand (`On-hand is now ${stock.onHand}.`) and `invalidateProductCaches` refreshes both the admin product detail and the storefront's `['products']` cache (a stock change alters the "From $X / in stock" the shopper sees).

#### The hooks + the openapi-typed client + `types.ts` aliases

Every admin page sits on a thin TanStack Query hook over the **openapi-fetch** typed `apiClient`, so request/response shapes come from the generated OpenAPI schema, not hand-written interfaces. Two conventions recur:

- **PascalCase query params.** The list hooks send `Page` / `PageSize` / `Status` / `Actor` etc., with a comment on each explaining why: "ASP.NET binds by property name, not the camelCase JSON policy." The query DTOs (`AdminOrderListQuery`, `AuditLogListQuery`) are PascalCase, and ASP.NET's query binding is by property name — so the FE must send PascalCase even though the JSON body policy is camelCase. (`useSalesReportQuery` takes no params — the server defaults to the last 30 days.)
- **Structured query-key factories.** `adminOrderKeys` / `adminUserKeys` / `auditKeys` expose `all` + `list(params)` (+ `detail(id)` for orders), so a write can `invalidateQueries({ queryKey: adminOrderKeys.all })` to blow away every list/detail under the namespace while individual entries stay independently cacheable — the same key-factory discipline Phase 2's `orderKeys` established.

`lib/api/types.ts` provides the ergonomic aliases the pages import, so feature code never spells out the generated path:

```ts
export type AdminUser = Schemas['AdminUserDto']
export type AdminOrderSummary = Schemas['AdminOrderSummaryDto']
export type AdminOrderDetail = Schemas['AdminOrderDetailDto']
export type AdminOrderPage = Schemas['AdminOrderSummaryDtoPagedResult']
export type AuditLog = Schemas['AuditLogDto']
export type SalesReport = Schemas['SalesReportDto']
```

The payoff: `AdminOrdersPage` imports `AdminOrderSummary` rather than `components['schemas']['AdminOrderSummaryDto']`, and because the aliases are *derived* from the generated schema, a backend DTO change reshapes the alias and surfaces as a **compile error in the page** — the typed client is the contract enforcement between the API and the SPA.

### The frontend — what to know cold

1. **`roleSets.ts` is a UX-only mirror of the backend policy matrix** — `ADMIN_AREA_ROLES` (the coarse `/admin` gate) + `ROLE_SETS` (per-capability) + the null-safe `hasAnyRole`. One map, three consumers (router, sidebar, per-button), `satisfies` keeps `AdminArea` a precise key union. **The server re-checks every request** — the frontend gate only hides what the user can't use.
2. **Two sibling shells, two-layer route gate.** `AdminShell` and `StorefrontShell` are top-level siblings (admin gets its own chrome, no storefront header); `/admin` is wrapped in a coarse `ADMIN_AREA_ROLES` `RoleGuard`, and **each child route further-gates via `ROLE_SETS.*`** — mirroring the backend's blanket-plus-per-endpoint policies.
3. **`SidebarNav` is the visible RBAC demo** — it filters `ITEMS` by `hasAnyRole(roles, ROLE_SETS[area])`, so Administrator / StoreManager / Staff each see a different nav, driven by the *same* map the router uses (so the sidebar and routes can't disagree).
4. **"Compose, not invent" primitives** — `DataTable<T>` is a generic, state-less, IoC table (`scope="col"` + `aria-label` baked in, parent owns fetch/page/filter); `Modal` delegates focus-trap/ESC/ARIA to Radix Dialog; `EmptyState` is the shared placeholder.
5. **Privilege escalation is defended on both sides** — `UsersPage` hides the "Store Manager" option for a non-administrator **and** the server 403s it (surfaced via `serverMessage`); the inventory-adjust modal mirrors the server's "reason ≤200, delta non-zero int" so bad input is caught before a 422.
6. **Refund is the canonical per-button capability gate** — `hasAnyRole(roles, ROLE_SETS.refund) && status === 'Paid'`; the accessible `RefundModal` states the **exact `formatCents` amount** in description and confirm button, disables while pending, and the action bar *is* the server-side state machine (ship only on `Paid`, deliver only on `Fulfilled`+`Shipped`).
7. **Audit before/after JSON renders safely into `<pre>` as escaped text** (`prettyJson` parse→stringify, fail-soft), never as HTML — untrusted stored content is never `dangerouslySetInnerHTML`.
8. **Hooks ride the openapi-typed client** — PascalCase query params (ASP.NET binds by property name), structured key factories (`all`/`list`/`detail`) for namespace invalidation, write-through on order mutations (`setQueryData(detail)` + invalidate list), and `types.ts` aliases that turn a backend DTO change into a compile error.

---

## 7. Chunk 4 — The testing surface + the CI gates

This is the chunk with no new feature in it — and the one an interviewer respects most, because it's the chunk that *proves* the other three. Phase 3 is the first time the project has **two test runners on the frontend** (Vitest for units/components, Playwright for browser E2E) and the first time **a real Chrome drives the admin workbench end-to-end in CI**. The central design problem the whole chunk solves: keep those two runners from stepping on each other, and make the E2E suite **hermetic** — no backend, no database, no Stripe — so it's deterministic on a fork PR. Then bolt the lot to `ci.yml` as merge-blocking gates with a hard coverage floor.

### What is in Chunk 4

```
src/web/
├─ vite.config.ts                       ← the Vitest `test` block: jsdom, setupFiles, the src-scoped include, v8 coverage
├─ src/test/setup.ts                     ← global setup: jest-dom matchers + RTL cleanup after each test
├─ src/lib/auth/roleSets.test.ts         ← locks the FE capability matrix to the backend policy matrix
├─ src/lib/format.test.ts                ← money cents↔dollars, the IEEE-754 float-drift regression
├─ src/components/ui/data-table.test.tsx ← DataTable render + empty-fallback (component test, jsdom)
├─ playwright.config.ts                  ← Vite-dev webServer, CI retries/workers, chromium-only project
├─ e2e/
│  ├─ storefront.spec.ts                 ← golden path: browse grid → open product detail
│  ├─ admin.spec.ts                      ← login → create product; mark-shipped → Fulfilled; axe a11y scan
│  └─ support/
│     ├─ mock-api.ts                      ← hermetic storefront /api/v1 stub (page.route)
│     └─ admin-mock.ts                    ← hermetic admin stub + the csrf-cookie seed + the stateful `shipped` flag
└─ (no new src feature files — this chunk is the test surface)

.github/workflows/ci.yml                 ← api coverage gate (≥85% line), the Vitest step, the dedicated e2e job
```

### Per-file purpose

#### `vite.config.ts` — the `test` block (the two-runner détente)

The single most load-bearing line in the chunk is the **include glob**:

```ts
test: {
  globals: true,
  environment: 'jsdom',
  setupFiles: ['./src/test/setup.ts'],
  css: false,
  // Vitest owns *.test.* under src; Playwright owns e2e/*.spec.ts ...
  include: ['src/**/*.test.{ts,tsx}'],
```

**Why scope `include` to `src/**/*.test.*` instead of leaving Vitest's default?** Playwright specs live in `e2e/` and are named `*.spec.ts`, and they `import { test } from '@playwright/test'`. If Vitest's default glob picked those up, it would try to run a Playwright spec inside jsdom — `@playwright/test`'s `test`/`expect` are a *different* `test`/`expect` than Vitest's, the browser-driver APIs aren't there, and the file would throw on import. The fix is a **convention split by file suffix**: `.test.*` is Vitest's, `.spec.ts` is Playwright's, and the `include` glob is what enforces it. This is the interview answer to "you have two test frameworks in one repo — how do they not collide?"

The rest of the block is deliberate too:
- **`environment: 'jsdom'`** gives component tests a DOM to render into without a real browser — fast and headless. The DataTable test renders real React markup against it.
- **`setupFiles: ['./src/test/setup.ts']`** runs once per test file before the tests (jest-dom matchers + cleanup, below).
- **`globals: true`** so `describe`/`it`/`expect` don't need importing per file (though the tests import them explicitly anyway — belt and suspenders).
- **`coverage` is `v8`** (the engine's native coverage, no instrumentation transform) and the `exclude` list strips the things that would *dilute* the percentage: the generated `src/lib/api/schema.d.ts`, all `*.d.ts`, the `src/test/**` harness, and the `src/main.tsx` bootstrap. The point is that the reported number reflects **hand-written, testable code**, not boilerplate you'd never unit-test.

(The `server.proxy` block — `/api → http://localhost:5124` — is the dev-time CORS/cookie workaround from earlier phases, and notably it's what the Playwright config *bypasses*: route interception happens in the browser, so the dev proxy is never reached.)

#### `src/test/setup.ts` — two jobs, both subtle

```ts
import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

afterEach(() => {
  cleanup()
})
```

The `/vitest` entrypoint (not the bare `@testing-library/jest-dom`) is the **Vitest-specific** build — it registers the jest-dom matchers (`toBeInTheDocument`, `toHaveTextContent`, …) *and* augments Vitest's `expect` type so those matchers **type-check** in the `.test.tsx` files. Import the wrong entrypoint and the matchers exist at runtime but TypeScript red-squiggles them.

The `afterEach(cleanup)` is the non-obvious half: **jsdom is shared across all tests in one file.** RTL's `render` mounts into that shared DOM; without `cleanup()` the previous test's markup lingers, so the next test's `screen.getByText('Alpha')` could match a *stale* node and either false-pass or throw on a duplicate match. Cleanup unmounts between tests so each starts from an empty DOM. This is exactly why the DataTable test can call `render(...)` twice (once for rows, once for empty) and have `queryByRole('table')` reliably return null in the second.

#### `src/lib/auth/roleSets.test.ts` — the capability-matrix lock

This is the highest-value unit test in the chunk because it guards a **cross-stack contract**, not a function. `roleSets.ts` is FE-only UX gating; the comment is explicit that "the server re-checks every request." So why test it at all? Because a drift between the FE map and the backend policy matrix (`PHASE_3_SCOPE.md §3.1`) is a **real UX bug** — a button rendered that the server then 403s. The test asserts the matrix cell-by-cell:

```ts
it('limits catalog management to Administrator only', () => {
  expect(hasAnyRole(['Staff'], ROLE_SETS.catalog)).toBe(false)
  expect(hasAnyRole(['StoreManager'], ROLE_SETS.catalog)).toBe(false)
  expect(hasAnyRole(['Administrator'], ROLE_SETS.catalog)).toBe(true)
})

it('limits refunds and user management to StoreManager and Administrator', () => {
  for (const set of [ROLE_SETS.refund, ROLE_SETS.users]) {
    expect(hasAnyRole(['Staff'], set)).toBe(false)
    expect(hasAnyRole(['StoreManager'], set)).toBe(true)
    expect(hasAnyRole(['Administrator'], set)).toBe(true)
  }
})
```

The three tiers it pins, straight from `roleSets.ts`: **catalog = `['Administrator']` only**; **refund + users = `['StoreManager', 'Administrator']`**; and the view-level areas (`orders`/`inventory`/`audit`/`reports`) all `toEqual(ADMIN_AREA_ROLES)` = all three back-office roles. That last assertion is clever — it doesn't re-type the array, it asserts the four view areas are *literally the same set* as `ADMIN_AREA_ROLES`, so adding a fourth role to the admin area in one place can't silently leave one area behind. The `hasAnyRole` edge cases (`undefined` roles → false, empty allowed list → false) lock the `?? false` fallback so an unauthenticated user is never accidentally admitted.

#### `src/lib/format.test.ts` — the float-drift regression

`format.ts` is the cents↔dollars boundary, and the headline test is the **IEEE-754 regression**:

```ts
it('rounds away binary float drift (0.1 * 100 !== 10 in IEEE-754)', () => {
  expect(dollarsToCents('0.10')).toBe(10)
  expect(dollarsToCents('29.30')).toBe(2930)
})
```

The "why" is the whole money-as-integer-cents doctrine of the project. `dollarsToCents` is `Math.round(value * 100)`; without the `Math.round`, `0.1 * 100` evaluates to `10.000000000000002` and `29.30 * 100` to `2929.9999…`, which `| 0`/`parseInt` would truncate to `2929` — a one-cent loss on a single line that compounds across an order. This test is a tripwire: if anyone ever "simplifies" the helper to drop the rounding, it goes red. The `centsToDollars(null/undefined) → ''` cases pin the form-field contract (an empty input, not the string `"NaN"`).

#### `src/components/ui/data-table.test.tsx` — the component test in jsdom

This is the chunk's one true *component* test (vs. pure-function units). It proves the generic `DataTable<Row>` both renders and degrades:

```tsx
render(<DataTable columns={columns} rows={rows} getRowKey={(row) => row.id} />)
expect(screen.getByText('Name')).toBeInTheDocument()
// header row + 2 body rows
expect(screen.getAllByRole('row')).toHaveLength(3)
```

The `getAllByRole('row')` length check is the meaningful one — it asserts **header + one row per item** (3 for 2 rows), which a naive "the text is on screen" assertion wouldn't catch if rows duplicated. The second test passes `rows={[]}` with an `empty={<p>Nothing here</p>}` slot and asserts the **empty fallback renders and the `<table>` does not** (`queryByRole('table')).not.toBeInTheDocument()`). That encodes the contract that an empty `DataTable` is an `EmptyState`, not a headers-only ghost table — and it only works reliably because `setup.ts`'s `cleanup()` tore down the first `render` before the second.

#### `playwright.config.ts` — the hermetic E2E harness

The config's job is to stand up a browser against the *real built SPA* but with *no backend*:

```ts
const isCI = !!process.env.CI
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: isCI,
  retries: isCI ? 1 : 0,
  workers: isCI ? 1 : undefined,
  reporter: isCI ? [['github'], ['html', { open: 'never' }]] : [['list']],
  use: { baseURL: 'http://localhost:5173', trace: 'on-first-retry' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'pnpm dev --port 5173',
    url: 'http://localhost:5173',
    reuseExistingServer: !isCI,
    timeout: 120_000,
  },
})
```

The design choices, all interview-grade:
- **`webServer` is the Vite *dev* server**, not a static `vite preview` of a production build, and not the ASP.NET API. Playwright boots it, waits for the URL, then runs. **The API is never started** — because the specs mock `/api/v1` *in the browser* (below), the request never leaves the page, so the dev proxy to `localhost:5124` is dead weight that's simply never exercised.
- **CI-conditional knobs.** `forbidOnly` fails the build if someone left a `test.only` in (a green-but-skipping suite is worse than red). `retries: 1` in CI absorbs a single flaky-timing hiccup without masking a real failure; `0` locally so you see flakes immediately. `workers: 1` in CI trades parallelism for determinism on a shared runner. `reuseExistingServer: !isCI` lets a local dev keep their already-running `pnpm dev`, but CI always starts clean.
- **`trace: 'on-first-retry'`** captures a full DOM/network trace only when a test retries — zero overhead on the happy path, a full post-mortem when something actually flaked.

#### `e2e/support/mock-api.ts` — the storefront hermetic stub

The hermetic principle: **one `page.route('**/api/v1/**', ...)` handler that switches on `pathname`**, returning the project's exact `ApiResponse<T>` envelope:

```ts
await page.route('**/api/v1/**', (route) => {
  const path = new URL(route.request().url()).pathname
  if (path.endsWith('/auth/me')) return route.fulfill(unauthorized())
  if (path.endsWith('/catalog/categories')) return route.fulfill(jsonOk([]))
  if (path.includes('/catalog/products/')) return route.fulfill(jsonOk(PRODUCT_DETAIL))
  if (path.endsWith('/catalog/products')) { /* paged list with one PRODUCT_SUMMARY */ }
  ...
  return route.fulfill(jsonOk(null)) // csrf and anything else
})
```

Two design notes the comment calls out. **A single switch handler, not many `page.route` calls**, deliberately — Playwright matches the *most-recently-registered* route first, so stacking multiple routes invites ordering surprises; one handler with explicit `endsWith`/`includes` branches is unambiguous. And the stub returns the **literal envelope shape** (`success/data/message/errors/traceId/timestamp`) so the real `apiClient`'s response unwrapping runs unchanged — the test exercises the actual client code, only the network is faked. `/auth/me → unauthorized()` keeps the storefront in its logged-out state for the golden path.

#### `e2e/support/admin-mock.ts` — the stateful admin stub + the CSRF gotcha

This is the richest mock and contains **the single most important gotcha in the chunk**:

```ts
// The apiClient does CSRF fail-fast on state-changing requests: it reads the (non-httpOnly) `csrf`
// cookie and echoes it as a header, throwing if absent. Seed one so POSTs (create/ship) go out.
await page.context().addCookies([{ name: 'csrf', value: 'e2e-csrf', url: 'http://localhost:5173' }])
```

**Why this line is non-negotiable:** the real `apiClient` implements double-submit CSRF by reading the non-httpOnly `csrf` cookie and echoing it as a header on every state-changing request, and it **fails fast (throws) if the cookie is absent** — by design, so a missing token surfaces locally instead of as a server 403. In the hermetic suite there's no `/csrf` endpoint setting that cookie, so **every POST in the admin spec (create product, mark-shipped) would throw before leaving the browser** unless the test seeds the cookie itself. Forget this one `addCookies` call and the storefront spec passes (it's all GETs) while the admin spec fails on the first POST — a confusing, isolated failure. Seeding `csrf` up front is what lets the create/ship flows actually fire.

The second design idea is the **stateful handler** — a closure `let shipped = false` flipped by the ship POST and read by the subsequent order-detail GET:

```ts
if (path.endsWith(`/admin/orders/${ADMIN_ORDER_ID}/ship`) && method === 'POST') {
  shipped = true
  return route.fulfill(ok(order(true)))
}
if (path.endsWith(`/admin/orders/${ADMIN_ORDER_ID}`)) return route.fulfill(ok(order(shipped)))
```

with `order(shipped)` returning `status: 'Paid'` before and `'Fulfilled'` (plus a populated `shipment`) after. This is what makes the **Paid → Fulfilled** transition *observable* end-to-end: the UI's optimistic refetch after the ship POST sees the now-shipped order, exactly as a real backend would behave. A static mock couldn't prove the transition; the one bit of in-memory state is the minimum needed to model a state machine.

#### `e2e/storefront.spec.ts` + `e2e/admin.spec.ts` — the golden paths + a11y

The **storefront** spec is the simplest possible proof the SPA boots and routes: land on `/`, assert the `Retail OMS` brand link, click the mocked `Aero Runner` product, assert the URL became `/products/aero-runner` and the detail heading rendered. It validates the shell + router + product query against the mock, nothing more — a smoke test that catches a white-screen-of-death regression.

The **admin** spec is the headline artifact of the phase. Three flows plus an a11y scan:

1. **Role-gated nav** — `goto('/admin')` and assert the sidebar nav shows Orders / Products / Users. Note the `page.getByRole('navigation')` scoping in the comment: *"Scope to the sidebar nav so a dashboard CTA like 'Manage products' isn't matched."* — a real selector-precision lesson, since the same word appears in a dashboard button.
2. **Create a product** — fill `#sku`/`#name`, `selectOption('cat-1')`, submit, then:

   ```ts
   // On success the page navigates to the new product's edit route (a persistent signal — the
   // success toast is transient, so we assert on the navigation instead).
   await expect(page).toHaveURL(/\/admin\/products\/new-prod-1$/)
   ```
3. **Mark-shipped → Fulfilled** — open order `#10042`, click `Mark shipped`, fill the dialog's `#ship-carrier`/`#ship-tracking`, confirm, then assert:

   ```ts
   // The detail refetches and the status badge flips to Fulfilled (persistent), proving the ship
   // succeeded — more robust than the transient "Marked shipped" toast.
   await expect(page.getByText('Fulfilled')).toBeVisible()
   ```

The recurring principle across both POST flows is **"assert a persistent signal, not a transient toast."** A success toast auto-dismisses on a timer, so an `expect(toast).toBeVisible()` is inherently racy — the test could miss it. Instead the spec asserts the *durable* consequence: a URL change (create) or a status badge that stays (ship). This is the difference between a flaky E2E and a trustworthy one, and it's the thing that makes the suite safe to gate a merge on.

The **a11y scan** uses `@axe-core/playwright`:

```ts
const results = await new AxeBuilder({ page })
  .withTags(['wcag2a', 'wcag2aa'])
  .disableRules(['color-contrast'])
  .analyze()
const serious = results.violations.filter(
  (v) => v.impact === 'serious' || v.impact === 'critical',
)
expect(serious, serious.map((v) => `${v.id}: ${v.help}`).join('\n')).toEqual([])
```

It runs axe against the live orders workbench, filters to `wcag2a`/`wcag2aa`, and **gates only on `serious`/`critical`** impact (info/minor are reported, not failed) — a pragmatic floor that catches real keyboard/screen-reader breakage without drowning the build in nitpicks. `color-contrast` is disabled because it's design-token territory, not a markup defect. The custom assertion message maps each violation to `id: help` so a failure tells you *what* broke, not just "expected [] to equal [{...}]".

#### `.github/workflows/ci.yml` — the gates

CI is **four parallel jobs** (`api`, `web`, `e2e`, `bicep`), each independently reportable, all merge-blocking. Chunk 4's contributions are the coverage gate, the Vitest step, and the dedicated Playwright job.

**The API coverage gate** is the hardest gate in the repo. Test runs with coverlet's XPlat collector, then ReportGenerator merges and enforces:

```yaml
- name: Test (with coverage)
  run: >
    dotnet test --no-build --configuration Release
    --logger "trx;LogFileName=test-results.trx"
    --collect:"XPlat Code Coverage"
    --results-directory ./TestResults

- name: Coverage report + 85% line gate
  run: |
    reportgenerator "-reports:./TestResults/**/coverage.cobertura.xml" ...
    line=$(grep -oP 'Line coverage:\s*\K[0-9.]+' ./TestResults/coverage/Summary.txt)
    awk -v c="$line" 'BEGIN { exit (c + 0 >= 85 ? 0 : 1) }' \
      || { echo "::error::Line coverage ${line}% is below the 85% gate"; exit 1; }
```

The mechanics worth knowing: each test project emits its own `coverage.cobertura.xml`, so **ReportGenerator's job is to *merge* them** into one summary (the glob `./TestResults/**/coverage.cobertura.xml` collects both). The gate parses `Line coverage:` out of the text summary and the `awk` line is the actual fail switch — `exit (c >= 85 ? 0 : 1)` — so a drop below **85%** (the project sits around **95–96%**) turns the job red. The comment is explicit that **branch coverage is reported but not gated** — a deliberate choice, because a hard branch gate punishes defensive `?:`/null-guards that are correct but not separately exercised. The `SummaryGithub.md` is appended to `$GITHUB_STEP_SUMMARY` so the number shows in the PR's checks tab.

**The Vitest gate** is one line in the `web` job, after typecheck/lint/format/build:

```yaml
- name: Test (Vitest)
  # Unit + component tests (jsdom). Fast, hermetic — no browser, no API.
  run: pnpm test
```

It lives with the static checks because it's *fast and headless* — no browser to install, no server to boot.

**The E2E gate is a separate job** precisely because it isn't:

```yaml
e2e:
  name: Web · E2E (Playwright)
  steps:
    ...
    - name: Install Playwright browser
      run: pnpm exec playwright install --with-deps chromium
    - name: E2E
      run: pnpm e2e
    - name: Upload Playwright report
      if: always()
      ...
```

The comment states the rationale: E2E needs **a browser binary + a running web server**, so it's slower than the static `web` checks and worth isolating — independent failure reporting, and it doesn't hold up the fast lint/build feedback loop. `playwright install --with-deps chromium` pulls just the one browser (the config's only project) **plus the OS libraries Chromium needs** on the bare runner. The Playwright HTML report uploads `if: always()` so a failed run leaves a debuggable trace artifact.

**Cross-cutting CI hygiene** worth a sentence at interview: every third-party action is **SHA-pinned with a `# vN` comment** (`actions/checkout@df4cb1c0…  # v6`) — a tag like `@v6` is mutable and a supply-chain risk; the SHA is immutable. `permissions: contents: read` caps the `GITHUB_TOKEN` to read-only because CI runs on fork PRs and holds no deploy secrets, and the `concurrency` group cancels stale runs on a new push.

### File relationship map — the two-runner split

```
                 src/web tests
                       │
        ┌──────────────┴───────────────┐
   *.test.{ts,tsx}                  e2e/*.spec.ts
   (Vitest, jsdom)                  (Playwright, real Chromium)
        │                                │
   vite.config.ts:                  playwright.config.ts:
   include:['src/**/*.test.*']      testDir:'./e2e', webServer=Vite dev
   environment:'jsdom'              project: chromium
   setupFiles → src/test/setup.ts        │
        │                                │  page.route('**/api/v1/**')
   ┌────┴─────┐                          ▼
 roleSets   format     data-table   e2e/support/{mock-api, admin-mock}
 .test      .test      .test.tsx    ── hermetic /api/v1 stub (NO backend)
   │          │           │            admin-mock SEEDS the `csrf` cookie
 capability  float     render+empty   (apiClient CSRF fail-fast on POST)
 matrix lock drift      (RTL+jsdom)         │
        │                                   ▼  stateful `shipped` flag
        ▼                              Paid ──ship POST──▶ Fulfilled
   ci.yml `web` job                   ci.yml `e2e` job
   pnpm test                          playwright install chromium → pnpm e2e

   ci.yml `api` job:  dotnet test --collect "XPlat Code Coverage"
                      → ReportGenerator merge → awk gate ≥85% line (~96%)
```

### Chunk 4 — what to know cold

1. **Two FE runners, split by file suffix.** Vitest owns `*.test.{ts,tsx}` (its `include` glob is scoped to `src/**` *on purpose*); Playwright owns `e2e/*.spec.ts`. Without the scoped glob, Vitest would try to run Playwright specs in jsdom and throw on import — the suffix convention is the whole defense.
2. **`setup.ts` does two things:** registers jest-dom's `/vitest` entrypoint (matchers + `expect` typings) and runs RTL `cleanup()` after each test, because **jsdom is shared within a file** and stale markup would leak between tests.
3. **The E2E suite is hermetic** — one `page.route('**/api/v1/**')` switch handler returns the real `ApiResponse<T>` envelope, so **no backend, DB, or Stripe** is needed; the Vite *dev* server is the only process, and route interception means the dev `/api` proxy is never hit.
4. **The CSRF gotcha:** `admin-mock` must `addCookies({ name: 'csrf' })` because `apiClient` does CSRF fail-fast — it reads the non-httpOnly `csrf` cookie and **throws on any POST if it's absent**. Storefront (all GETs) passes without it; the admin create/ship POSTs would die before leaving the browser.
5. **Assert persistent signals, not toasts.** Create asserts the **URL navigation** to the new edit route; ship asserts the **`Fulfilled` badge** (backed by `admin-mock`'s stateful `shipped` flag) — both durable, unlike the auto-dismissing success toast.
6. **`roleSets.test` is a contract lock, not a function test** — it pins catalog=Administrator-only, refund+users=StoreManager+Administrator, view areas=all three, so FE/BE matrix drift (a button the server then 403s) goes red. `format.test` pins the `Math.round` that kills `0.1*100 → 10.000…2` float drift.
7. **a11y is gated on `serious`/`critical` only** (axe, `wcag2a`/`wcag2aa`, `color-contrast` disabled) — a pragmatic floor, with a violation-list assertion message.
8. **CI = four parallel jobs.** The API job gates **≥85% line coverage** (currently ~96%) via coverlet `--collect "XPlat Code Coverage"` → ReportGenerator merge → an `awk` exit-code switch (branch coverage reported, not gated); Vitest rides the fast `web` job; Playwright is a **dedicated `e2e` job** that installs chromium `--with-deps`. All actions are **SHA-pinned**, `GITHUB_TOKEN` is `contents: read`.

---

## 8. The review + fixes pass

After the four chunks (0–4) landed, Phase 3 went through a **7-dimension adversarial review** — RBAC, audit/PII, money/correctness, security, backend-quality, frontend/a11y, and completeness, each finding **adversarially verified** before a synthesis pass decided fix-vs-defer. The verdict: **0 critical, 1 high, 1 medium, ~15 low**, of which **24 were confirmed/partial and 2 were refuted** after verification. The security dimension came back **clean** — the privilege-escalation surface was closed (a forged but validly-signed Customer JWT is rejected at *authorization*, not just authentication), and there was no injection, no leaked secret, and no mass-assignment hole. As in Phase 2, **0 critical/high is the strong signal**: the architecture held, and the work that followed was completeness and hygiene, not firefighting.

### What held up (the good signal)

- **The policy matrix is data, defined once.** `Roles.Policies` names each capability as a constant (`Orders.View`, `Orders.Refund`, `Catalog.Manage`, …) and `Program.cs` binds each to a `RequireRole(...)` in a single `AddAuthorization` block — `OrdersRefund` is `StoreManager+Administrator`, `CatalogManage` is `Administrator`-only, the rest are `staffPlus`. A rule change is one edit, and `[Authorize(Policy = Roles.Policies.X)]` on the controller does the rest — no `Roles = "StoreManager,Administrator"` strings scattered across endpoints to drift out of sync.
- **Race-free StoreManager-create off the JWT claim.** The "who may create which role" decision is made from the caller's *token role*, never a request field — so a Staff account can't escalate by naming `StoreManager` in the body. The forged-JWT test (`AdminUsers_ForgedCustomerJwt_Returns403`) proves the gate is on the role claim, minting a Customer token directly via `IJwtService` (bypassing login) and still getting a **403**.
- **The dual-interceptor audit is atomic and correctly ordered.** Both interceptors hook one `SaveChanges` pipeline, and the order is load-bearing: `AuditingInterceptor` stamps `CreatedBy/UpdatedAt` **first** so the `AuditTrailInterceptor`'s before/after snapshot reflects the *final* stamped values — `options.AddInterceptors(AuditingInterceptor, AuditTrailInterceptor)`. The trail row and the business write commit in the same transaction; a rolled-back write leaves no orphan audit row.
- **Exactly-one-winner refund claim.** The Stripe provider is hit at most once per order because the `Paid → Refunding` transition is a RowVersion-guarded set-based claim; a concurrent second cancel loses the guard and 409s. (The concurrent-ship test `Ship_ConcurrentRequests_OneShipsOneConflicts` proves the same `Order.RowVersion`/status guard on the fulfillment transition — exactly one 200, the loser 409.)
- **`ROLE_SETS` mirrors the backend.** The frontend `roleSets.ts` is a deliberate copy of the capability matrix (`refund: ['StoreManager','Administrator']`, `catalog: ['Administrator']`, …) with the comment that **frontend auth is UX-only — the server re-checks every request**. The route guards, the sidebar, and per-element gating all read this one map, so the admin shell never advertises an area the user's role can't actually use, and there's nothing to drift.

### What got fixed (mapped to commits)

| Fix | What it was | Commit |
|---|---|---|
| **M1** | Inventory-adjust guarded only `OnHand ≥ 0`; a negative delta could oversell units already `Reserved` (which `CommitReservedAsync` then drives on-hand negative) → reject when `after < item.Reserved` | `4c180fe` (batch A) |
| **L1/L2/L9** | Reversed `[from,to]` ranges returned a silent empty-200; `?status=99` resolved to an always-empty page → new `DateRangeGuard` (422 on `from > to`, report span capped at 366 days) + `ParseStatus` adds `Enum.IsDefined` | `4c180fe` (batch A) |
| **L4** | Refund **restock** was a set-based `ExecuteUpdate` invisible to the interceptor → emit a `Restocked` `InventoryItem` row per line (keyed on the same item id a manual `InventoryAdjusted` uses) on both admin + webhook paths | `45f29b1` (batch B) |
| **L6** | The named `Refund` `Order` row was staged in the wrong place with a hard-coded `Paid` prior status → moved into `OrderRefundService` at the **true idempotent transition**, recorded once with the real prior status (`Paid` *or* `Refunding`) | `45f29b1` (batch B) |
| **L5** | A failed Stripe refund rolled back the claim with no audit evidence → record `RefundClaimed` (Paid→Refunding) and, on failure, `RefundClaimReleased` (Refunding→Paid) + flush | `45f29b1` (batch B) |
| **L3** | Defensive self-trail guard — the append-only ledger must never trail itself → `&& e.Metadata.ClrType != typeof(AuditLog)` in the monitored snapshot | `45f29b1` (batch B) |
| **L8** | `AdminUserService.ListAsync` materialized the **whole** user table to return one page → the no-role-filter path now does `COUNT + OFFSET/FETCH` in `IQueryable` | `458ba0e` (batch C) |
| **L7/L10/L11/L12** | FE polish: adjust-reason `maxLength=200` to mirror the server (no 422 bounce); native `window.confirm` → accessible Radix `RefundModal`; `DataTable` header cells get `scope="col"` + `aria-label`; sales-chart tooltip formats currency + labels the series | `d6ce914` (batch D) |
| **L15/L16** | Named-but-missing tests: forged-Customer-JWT → **403**, and two concurrent ship POSTs → one **200** / one **409** (`Order.RowVersion`) | `0691028` (batch E1) |
| **H1** | The admin area had **zero browser coverage** → hermetic `admin.spec.ts` (login as Administrator → role-gated nav → create product → mark shipped flips the badge to Fulfilled → axe scan) | `95219f6` (batch E) |
| **L13/L14/L17** | Scope-fidelity items reconciled in `PHASE_3_SCOPE.md §16.1` rather than built speculatively: 13 primitives shipped (Drawer/Checkbox/Tabs deferred), `FilterPanel` stays storefront-located, inventory-adjust ships under the variant editor | `5c92af5` (docs reconcile) |

The two **refuted** findings are worth keeping in mind for interviews: the headline correctness claims (no double-refund, no oversell) were challenged and *held*, because the money-moving transitions are already RowVersion-guarded — the review's job there was to confirm the guard, not add one.

The deepest cluster (L4/L5/L6) is the same lesson Phase 2 learned about `ExecuteUpdate`: **set-based writes bypass the change tracker, so they bypass the audit interceptor too.** Phase 2 fixed this for the *stamp* fields (`UpdatedAt`/`UpdatedBy` by hand); Phase 3 closes it for the *trail* — every set-based refund/restock/claim write now emits an explicit `IAuditWriter.Record(...)` row at the real transition, so a forensic reader sees a refund restock as plainly as a manual stock adjustment. The `DateRangeGuard` (L1/L2) generalizes the same "a reversed range reads as no-data with a 200 — a silent client error" insight across all three date-filtered admin surfaces (workbench, audit, report) in one shared helper.

### The review/fixes — what to know cold

1. **7-dimension adversarial review → adversarial verify → synthesis; 0 critical, 1 high (H1: no admin E2E), 1 medium (M1: reserved-aware inventory guard), ~15 low; 24 confirmed/partial, 2 refuted; security clean.**
2. **M1 is the one real correctness fix** — the inventory guard now rejects `after < Reserved` (Available < 0), not merely on-hand < 0, so a negative adjustment can't oversell stock already promised to in-flight checkouts.
3. **The audit-completeness cluster (L3/L4/L5/L6)** closes the `ExecuteUpdate`-bypasses-the-interceptor gap: explicit `Restocked` / `RefundClaimed` / `RefundClaimReleased` rows + the named `Refund` row recorded once at the true transition with the true prior status.
4. **H1 (admin E2E) and L15/L16 (forged-JWT 403, concurrent-ship 409)** were named-but-missing test artifacts — added as hermetic, API-mocked specs that assert on persistent signals (navigation, status badge), not transient toasts.
5. **L13/L14/L17 were reconciled in docs, not built** — deferring unused primitives is the *correct* quality call (shipping them would read as dead code), and §16.1 records that honestly.
6. **Final tally: 191 backend + 17 vitest + 7 e2e green; CI line coverage ~96%** (well clear of the 85% Coverlet gate flipped at phase-end).

Section written. The output above is the complete `## 8. The review + fixes pass` section, grounded in:
- `docs/PHASE_3_SCOPE.md` §16/§16.1 (verdict, deferrals L13/L14/L17, fix list)
- `git log` commit hashes: batch A `4c180fe`, B `45f29b1`, C `458ba0e`, D `d6ce914`, E1 `0691028`, E `95219f6`, docs reconcile `5c92af5`
- verbatim code from `AdminInventoryService.cs` (M1), `OrderRefundService.cs`/`AdminOrderService.cs` (L4/L5/L6), `AuditTrailInterceptor.cs` (L3), `DateRangeGuard.cs` (L1/L2), `AdminUserService.cs` (L8), the E1 tests, `Roles.cs`/`Program.cs` (policy matrix + dual-interceptor ordering), and `roleSets.ts` (ROLE_SETS mirror).

---

## 9. File relationship maps

These trace the real Phase 3 flows end to end. Names, methods, policies, routes, and SQL are exactly as they appear in the code. (Phase 2's cart/checkout/refund maps still hold — these add the admin/RBAC/audit/report layer on top.)

### An admin request → policy → controller → service → audit (the spine)

```
SPA admin call → apiClient (httpOnly access_token cookie + X-CSRF-Token on writes)
   │
   ▼
JwtBearer  (Program.cs: DefaultScheme = Bearer, the ONLY scheme)
   │  OnMessageReceived: context.Token = Request.Cookies["access_token"]   ← token comes from the COOKIE, not the Authorization header
   │  TokenValidationParameters: Validate{Issuer,Audience,Lifetime,IssuerSigningKey} ALL true
   │     └─ emits ClaimTypes.Role for each of the user's roles
   ▼
[Authorize(Policy = Roles.Policies.OrdersView)]   on AdminOrdersController.ListOrders
   │  policy resolved from AddAuthorization: OrdersView → RequireRole(staffPlus = {Staff, StoreManager, Administrator})
   │     no token        → 401      role not in set → 403      (UseAuthentication → UseAuthorization)
   ▼
AdminOrderService  (reads NOT owner-scoped — staff see every order; the policy IS the gate)
   │  for a MUTATION (ship/deliver/refund): mutate tracked entity + _audit.Record("...", before, after)
   │     └─ AuditWriter just _db.Set<AuditLog>().Add(row)  — it does NOT SaveChanges
   ▼
_orders.SaveChangesAsync(ct)   ← ONE SaveChanges, ONE transaction
   │
   ▼
RetailDbContext SaveChanges pipeline — TWO interceptors compose, order is load-bearing:
   1. AuditingInterceptor    stamps CreatedBy / UpdatedAt on the changing row FIRST
   2. AuditTrailInterceptor  Capture(): for every Added/Modified/Deleted entry whose ClrType ∈
                             {Product, InventoryItem, Order, Payment, Shipment} → Add an AuditLog
                             {Actor, Action, before/after JSON (PII redacted to "***")} to the SAME ChangeTracker
   ▼
SQL Server — the business row, the AuditWriter's named row, AND the interceptor's auto-rows
             all INSERT in the same transaction (a rolled-back change rolls back its audit too)
```

> Two audit sources, one table. `AuditWriter.Record(...)` writes a **named business-event** row (`"Shipped"`, `"Refund"`) the service chooses; `AuditTrailInterceptor` writes a **structural** row (`"Insert"`/`"Update"`/`"Delete"`) for every monitored-entity change automatically. Both ride the caller's `SaveChanges`, so neither needs its own transaction.

### Mark shipped → Fulfilled + Shipment + audit

```
SPA "Mark shipped" → POST /api/v1/admin/orders/{id}/ship  { carrier, trackingNumber }
   │  CsrfMiddleware (write) + JwtBearer
   ▼
AdminOrdersController.MarkShipped  [Authorize(Policy = Roles.Policies.OrdersFulfill)]   ← staffPlus
   │  ValidateAsync(_shipValidator) → 422 on bad carrier/tracking
   ▼
AdminOrderService.MarkShippedAsync(id, request)
   │  order = _orders.GetTrackedWithShipmentAsync(id)   ← TRACKED (.Include(Shipment)), null ⇒ 404
   │  order.Status != Paid          ⇒ ConflictException 409  ("must be Paid to ship")
   │  order.Shipment is not null     ⇒ ConflictException 409  ("already has a shipment")
   │
   │  order.Shipment = new Shipment { Carrier, TrackingNumber, Status = Shipped, ShippedAt = now }
   │  order.Status   = OrderStatus.Fulfilled
   │  _audit.Record("Shipped", nameof(Order), id,
   │                 before:{ Status="Paid" }, after:{ Status="Fulfilled", Carrier, TrackingNumber })
   ▼
_orders.SaveChangesAsync(ct)   ← ONE SaveChanges
   │  Order.RowVersion guards the Paid→Fulfilled flip:
   │     a concurrent second ship (read the same RowVersion) affects 0 rows on save → DbUpdateConcurrencyException → 409
   │  AuditTrailInterceptor auto-adds: Order "Update" row + Shipment "Insert" row
   ▼  (then GetAsync re-reads the AdminOrderDetailDto to return)
200 ApiResponse<AdminOrderDetailDto>   →   AuditLog now holds 3 rows: named "Shipped" + auto Order-Update + auto Shipment-Insert
```

> `deliver` is the deliberate contrast: it advances only the `Shipment.Status` (the order stays `Fulfilled`), so a concurrent double-deliver is benign last-write-wins — **no `RowVersion` guard**, only the ship + refund *order-status* transitions are concurrency-gated.

### Admin refund → claim → Stripe → reversal → restock + audit

```
SPA "Refund" → POST /api/v1/admin/orders/{id}/refund    [Authorize(Policy = Roles.Policies.OrdersRefund)]   ← managerPlus (StoreManager + Administrator)
   ▼
AdminOrderService.RefundAsync(id)
   │  order = GetDetailForAdminAsync(id)        null ⇒ 404
   │  Status ∉ {Paid, Refunding} ⇒ 409          (Refunding = the re-drivable RECOVERY state)
   │  paymentIntentId = GetChargePaymentIntentIdAsync(id)   (the Payment with AmountCents > 0)   null ⇒ 409
   │
   │  if Status == Paid:                                                       ┌─ TOCTOU claim ─┐
   │     TryClaimForRefundByIdAsync(id, now, actor):                          │ caps Stripe to │
   │        UPDATE Order SET Status=Refunding WHERE Id=id AND Status=Paid     │ ONE call per   │
   │        (set-based ExecuteUpdate → bypasses BOTH interceptors)            │ order          │
   │        affected == 1 ? justClaimed=true  :  409  ("can't be refunded")   └────────────────┘
   │     _audit.Record("RefundClaimed", before:{Paid}, after:{Refunding})    ← STAGED (ExecuteUpdate emits no auto-row)
   │
   │  try   _refundGateway.RefundAsync(pi)        ← Stripe, IdempotencyKey = "refund:{pi}"  (deterministic)
   │  catch (Stripe failed, no money moved):
   │     if justClaimed:
   │        ReleaseRefundClaimToAsync(id, OrderStatus.Paid)   (Refunding → Paid; ExecuteUpdate)
   │        _audit.Record("RefundClaimReleased", before:{Refunding}, after:{Paid})
   │        SaveChangesAsync()   ← flush claim+release evidence (the rollback leaves no other write)
   │     throw   ← order stays refundable
   │
   │  ── money is back ──> _orderRefund.RefundByPaymentIntentAsync(pi)   (the SAME reversal the webhook uses)
   │       order = GetByPaymentIntentIdAsync(pi)
   │       Status == Refunded ? return   ← idempotent no-op (a concurrent charge.refunded webhook already did it)
   │       priorStatus = Status (Paid or Refunding)
   │       BeginTransaction:
   │          per line: RestockByVariantAsync(variantId, qty)  (ExecuteUpdate OnHand += qty)
   │                    + _audit.Record("Restocked", nameof(InventoryItem), itemId, after:{ qty, OrderId, Reason="OrderRefunded" })
   │          order.Status = Refunded
   │          order.Payments.Add(Payment { AmountCents = -TotalCents, Status=Refunded })   ← negative = refund, append-only ledger
   │          _audit.Record("Refund", before:{ priorStatus }, after:{ Refunded })   ← named row, ONCE, with the TRUE prior status
   │          SaveChangesAsync()   ← Order.RowVersion serializes vs the webhook → stale writer 0 rows → 409, never double-restock
   │          tx.Commit()
   │
   │  SaveChangesAsync()   ← flush any staged row (e.g. RefundClaimed) the reversal short-circuited past
   ▼
200 AdminOrderDetailDto
```

> Why two claim methods on the repo: `TryClaimForRefundByIdAsync` (admin, **no** owner filter) vs `TryClaimForRefundAsync` (customer-cancel, `&& CustomerProfileId == profileId`). Both flip `Paid → Refunding` via a `WHERE … Status == Paid` set-based `ExecuteUpdate`, so `affected == 1` is the single winner — the same primitive as Phase 2's last-unit reservation race.

### Sales-by-day aggregation

```
SPA Reports → GET /api/v1/analytics/sales-by-day?from&to    [Authorize(Policy = Roles.Policies.ReportsView)]   ← staffPlus
   ▼
AnalyticsController.SalesByDay([FromQuery] SalesByDayQuery)
   │  to   = query.To   ?? now            ← default LAST 30 DAYS
   │  from = query.From ?? to.AddDays(-30)
   │  DateRangeGuard.Validate(from, to, maxSpanDays: 366)   ← validates the EFFECTIVE (post-default) range:
   │     reversed range / span > 366 days ⇒ 422   (an only-`from` request can't silently widen to "now"; caps the in-memory work)
   ▼
ReportQueryService.GetSalesByDayAsync(from, to)
   │  orders = Orders.AsNoTracking()
   │     .Where(PaidStatuses.Contains(Status) && PlacedAt >= from && PlacedAt < to)   ← PaidStatuses = {Paid, Fulfilled}
   │     .Include(Lines).ThenInclude(ProductVariant).ThenInclude(Product).ThenInclude(Category)   ← 4-level include
   │     .ToListAsync()   ← LOAD into memory, then aggregate (no EF date-GROUP-BY translation; trivial at portfolio scale)
   │
   │  days       = orders.GroupBy(DateOnly.FromDateTime(PlacedAt.UtcDateTime))
   │                      .Select(g => DailySalesDto("yyyy-MM-dd", g.Count(), g.Sum((long)TotalCents)))
   │  categories = orders.SelectMany(Lines)
   │                      .GroupBy(line.ProductVariant?.Product?.Category?.Name ?? "(uncategorised)")   ← soft-deleted product ⇒ "(uncategorised)"
   │                      .Select(g => CategorySalesDto(name, g.Sum((long)LineTotalCents)))
   ▼  SalesReportDto(days, categories)   — money summed in `long`
200 ApiResponse<SalesReportDto>   →   Recharts (line chart over days + bar over categories)
```

> The `< to` (exclusive) upper bound mirrors the admin-list filter, so day-bucketing never double-counts a boundary instant; the `GroupBy` is on `UtcDateTime` so the day label is stable regardless of the row's offset.

### Admin login → /auth/me → role-gated nav

```
app mount
   ▼
SessionBootstrapper (useEffect, once)
   │  startEpoch = getAuthEpoch()
   │  GET /auth/csrf        ← seed the double-submit cookie so later admin writes carry X-CSRF-Token
   │  GET /auth/me
   │     getAuthEpoch() !== startEpoch ? drop   ← a login/logout won the race (the "bounced to /login" guard)
   │     data?.data ? applyAuthUser(dto)  :  setLoading(false)   ← anonymous 401: DON'T setUser(null) (would bump the epoch)
   ▼
applyAuthUser → useAuthStore.setUser({ id, email, roles: dto.roles ?? [] }); isLoading = false
   ▼
Route element wrapped in <RoleGuard allowedRoles={ADMIN_AREA_ROLES /* or ROLE_SETS.orders, … */}>
   │  isLoading ? return null            ← render NOTHING while /auth/me is in flight (no flash of admin UI, no flash of public)
   │  !user     ? <Navigate to="/login" state={{ from }} replace />
   │  allowedRoles.length && !user.roles.some(r => allowedRoles.includes(r)) ? <Navigate to="/login" replace />   ← 403-equivalent
   ▼
AdminShell  (its OWN surface — no storefront header; PHASE_3_SCOPE §12)
   └─ SidebarNav
        roles = useAuthStore(s => s.user?.roles)
        ITEMS.filter(item => item.area === undefined || hasAnyRole(roles, ROLE_SETS[item.area]))
           └─ Dashboard (no area) always shows; Orders→ROLE_SETS.orders, Users→ROLE_SETS.users (Manager+),
              Products→ROLE_SETS.catalog (Admin-only)  →  each role sees a DIFFERENT sidebar
```

> The FE gate is **UX-only** and mirrors the backend matrix from one source: `roleSets.ts` (`ROLE_SETS` + `hasAnyRole`) is the client twin of `Roles.Policies.*` + `RequireRole`. Hiding the nav item and short-circuiting the route prevent a flash and a wasted 403 — but the `[Authorize(Policy = …)]` on the controller is the actual gate; a hand-crafted request to `/admin/orders` still 403s server-side.

---
```

---

## 10. Patterns to remember (interview material)

The Phase 3 additions to your interview toolkit, in rough priority order. (Phase 0's envelope/middleware/audit-*stamp* interceptor, Phase 1's cookie-JWT/CSRF/soft-delete, and Phase 2's webhook-idempotency/`rowversion`/snapshot patterns all still hold; these build on them — and several are the *staff-side* mirror of a customer-side Phase 2 pattern.)

### 1. Policy-based RBAC: named capabilities in one block (highest priority)

**The pattern:** every admin capability is a **named authorization policy** — `Roles.Policies.OrdersRefund`, `Roles.Policies.InventoryAdjust`, … — defined **once** in a single `AddAuthorization` block in `Program.cs`, each a `RequireRole(...)` over the canonical `Roles.*` names, and applied as `[Authorize(Policy = Roles.Policies.X)]`. The role→capability mapping lives in three reusable arrays:

```csharp
string[] staffPlus = { Roles.Staff, Roles.StoreManager, Roles.Administrator };
string[] managerPlus = { Roles.StoreManager, Roles.Administrator };
string[] adminOnly = { Roles.Administrator };
// ...
options.AddPolicy(Roles.Policies.OrdersRefund, p => p.RequireRole(managerPlus));
options.AddPolicy(Roles.Policies.CatalogManage, p => p.RequireRole(adminOnly));
```

**Why it matters:** the real matrix is **capability-shaped and overlapping** — refund = manager+admin, fulfil = staff+manager+admin, catalog = admin-only. Scattering `[Authorize(Roles = "StoreManager,Administrator")]` strings across controllers means a rule change is a find-replace across files, and one typo (`"Adminstrator"`) compiles fine then fails at runtime. A named policy makes a rule change **one edit** in one place. The JWT already emits `ClaimTypes.Role`, so this needs no token change.

**Interview gotcha:** storefront `[Authorize(Roles = Customer)]` attributes stay *role*-based on purpose — only the *admin* matrix is policy-based, because only it is overlapping. `Roles.Policies.AuditExport`/`ReportsExport` are defined-but-unused — that's what makes "Staff is read-only on audit/reports" a *real* tier the moment an export button ships.

**Resume claim:** "3-role RBAC as a single-source-of-truth named-policy matrix — capability changes are one edit, not a controller-wide find-replace of role strings." (`Roles.cs`, `Program.cs`)

### 2. The dual-interceptor audit trail: stamp vs append

**The pattern:** Phase 0's `AuditingInterceptor` **stamps** `CreatedBy`/`UpdatedAt` columns *on the changing row*. Phase 3 adds a **second, composed** `SaveChangesInterceptor` — `AuditTrailInterceptor` — that **appends** an immutable `AuditLog` history row (`Actor` + before/after JSON) for every Insert/Update/Delete of a *monitored* entity:

```csharp
private static readonly HashSet<Type> Monitored = new()
{ typeof(Product), typeof(InventoryItem), typeof(Order), typeof(Payment), typeof(Shipment) };
```

It captures **in `SavingChanges`, in one pass, no post-save second save** — snapshotting the entries to a `List` first (you must not mutate the `ChangeTracker` you're iterating), then `context.Set<AuditLog>().AddRange(rows)` so the audit rows **INSERT in the same `SaveChanges`, inside the same transaction** as the change they describe. A rolled-back business change rolls back its audit row too.

**Why it matters / interview gotcha:** the one-pass capture is only possible because **every monitored entity has a client-generated `Guid` PK** — the `EntityId` is known *before* the SQL runs, so there's no post-save hook needed to learn the key. The DB-generated columns it deliberately *doesn't* audit (`OrderNumber`, `RowVersion`) are exactly the ones not yet materialised. And it **redacts PII** before the JSON ever lands in an admin-readable table:

```csharp
private static readonly HashSet<string> Redacted = new(StringComparer.OrdinalIgnoreCase)
{ "GuestEmail", "Email", "Password", "PasswordHash", "Token", "Secret",
  "ShippingAddress", "BillingAddress", "RawPayloadJson" };
```

`AuditLog` is itself excluded from `Monitored` (recursion safety — the ledger must never trail itself).

**Resume claim:** "Append-only audit trail via a composed EF interceptor — actor + redacted before/after JSON, atomic with the change, no second round-trip."

### 3. Set-based writes carry an explicit audit obligation

**The pattern:** `ExecuteUpdate` (the optimistic-concurrency primitive) **bypasses the change tracker**, so the `AuditTrailInterceptor` never sees it. Every admin state change that goes through a set-based write therefore **emits an explicit `IAuditWriter` row by hand** — named, legible business events the generic CRUD capture can't produce:

```csharp
// AdminInventoryService.AdjustAsync
_audit.Record("InventoryAdjusted", nameof(InventoryItem), item.Id.ToString(),
    before: new { OnHand = before }, after: new { OnHand = after, request.Delta, request.Reason });
```

`OrderRefundService` emits per-variant `"Restocked"` rows (because `RestockByVariantAsync` is `ExecuteUpdate`); `AdminOrderService.RefundAsync` emits `"RefundClaimed"` for the `Paid → Refunding` flip (`TryClaimForRefundByIdAsync` is `ExecuteUpdate`).

**Why it matters:** this is **the exact gap the adversarial review hunted** (review batch B, "close the refund/restock audit-trail gaps", `45f29b1`). A refund's restock is a `Reserved`/`OnHand` mutation that leaves *no* interceptor trace — without the hand-written row it would be a silent inventory change. `IAuditWriter.Record` only `Add`s to the *current* `RetailDbContext`; it does **not** `SaveChanges`, so the row still commits atomically inside the caller's unit of work.

**Interview gotcha:** the named row carries the **true prior status** (`before: new { Status = priorStatus.ToString() }`), recorded *at the actual idempotent transition* — so a recovery re-drive can't duplicate it and a `Refunding → Refunded` isn't misreported as `Paid → Refunded`.

**Resume claim:** "Closed the audit blind spot where set-based `ExecuteUpdate` bypasses the interceptor — every admin state change emits an explicit, named audit row."

### 4. Optimistic concurrency on admin transitions (the staff-side mirror)

**The pattern:** admin state transitions are guarded the same way customer ones were in Phase 2. **Ship** is a tracked write — `order.Status = OrderStatus.Fulfilled` + new `Shipment` in one `SaveChanges` — where **`Order.RowVersion` makes the flip concurrency-safe (a stale write → 409)**. **Refund** uses a set-based, exactly-one-winner TOCTOU claim:

```csharp
int affected = await _db.Orders
    .Where(o => o.Id == orderId && o.Status == OrderStatus.Paid)
    .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Refunding) /* ... */, ct);
return affected == 1;
```

The `Paid → Refunding` claim runs **before** the Stripe call, so **exactly one refund reaches the payment provider**. On Stripe failure, `ReleaseRefundClaimToAsync` reverts `Refunding → Paid` so the order stays refundable.

**Why it matters:** `Refunding` is reused here as a **re-drivable recovery state** — `RefundAsync` accepts an order already in `Refunding` (`order.Status is not (OrderStatus.Paid or OrderStatus.Refunding)`) because a prior attempt that refunded at Stripe but failed the local reversal is safely re-driven (every downstream step is idempotent). The concurrent-ship-409 and forged-JWT-403 tests (`0691028`, review batch E1) are the proof artifacts.

**Interview gotcha:** the `Paid`-only filter is deliberate — a **shipped/`Fulfilled` order is a return/RMA case (deferred)**; refunding it would restock goods already with the customer and strand the shipment.

**Resume claim:** "Concurrency-safe admin ship/refund via `rowversion` + an atomic `Paid → Refunding` claim that caps the payment provider to exactly one refund."

### 5. The reserved-aware inventory guard

**The pattern:** the manual stock-adjust rejects anything that would make **`Available` (= `OnHand − Reserved`) negative — not merely `OnHand < 0`:**

```csharp
if (after < item.Reserved)
{
    throw new ConflictException(
        $"Adjustment would leave on-hand ({after}) below reserved stock ({item.Reserved}), "
        + $"making available negative ...");
}
```

**Why it matters:** `Reserved` units are **already promised to in-flight checkouts**. Letting on-hand drop below them oversells, and **breaks the ledger when those reservations commit** — `CommitReservedAsync` subtracts `OnHand` *unconditionally*, so an on-hand that's too low goes negative at payment time. "Positive on-hand" isn't enough; the *sellable* figure is what must stay `≥ 0`. (With `Reserved = 0` this collapses to the naïve on-hand ≥ 0 guard — a clean superset.)

**Interview gotcha:** this is review batch A (`4c180fe`) — the first guard only checked `OnHand`, which passed for a row with held stock and corrupted it at commit.

**Resume claim:** "Inventory adjust guards `Available = OnHand − Reserved`, not raw on-hand — preventing oversell against units already committed to in-flight checkouts."

### 6. The frontend capability mirror — and the check a static policy can't express

**The pattern:** the React app mirrors the backend policy matrix in **one file**, explicitly UX-only:

```ts
// roleSets.ts — "Frontend auth is UX-only — the server re-checks every request"
export const ROLE_SETS = {
  orders: ['Staff', 'StoreManager', 'Administrator'],
  users: ['StoreManager', 'Administrator'],
  catalog: ['Administrator'],
  refund: ['StoreManager', 'Administrator'], // mirrors Orders.Refund
} satisfies Record<string, string[]>
```

`ROLE_SETS` drives the route guards, the sidebar, and per-element gating consistently — but the server is the gate. **The hidden button is convenience; the policy attribute is security.**

**Why it matters / interview gotcha:** one rule is **body-dependent** and a static policy can't express it — *an Administrator may create a StoreManager, but a StoreManager may only create Staff.* `[Authorize(Policy = Users.ManageStaff)]` lets both roles in; the differentiation is an explicit runtime check **on the request body**:

```csharp
if (request.Role == Roles.StoreManager && !User.IsInRole(Roles.Administrator))
    return StatusCode(StatusCodes.Status403Forbidden, /* ... */);
```

A declarative policy gates on *the caller's* claims, not *the payload it's asking to create* — so this authority check is, correctly, controller code.

**Resume claim:** "Role→capability mapping in one frontend module mirroring the backend policies — with the body-dependent privilege-escalation guard (a manager can't mint a manager) enforced server-side where a static policy can't reach."

### 7. Hermetic Playwright via `page.route` API mocking

**The pattern:** the E2E suite stubs **every** `/api/v1` call through Playwright route mocking, so it runs with **no backend and no database** in CI. One handler switches on the path (avoiding Playwright's later-route-matched-first ordering surprise), and stateful flows use a closure flag so a write is reflected by the next read:

```ts
let shipped = false
if (path.endsWith(`/admin/orders/${ADMIN_ORDER_ID}/ship`) && method === 'POST') {
  shipped = true
  return route.fulfill(ok(order(true)))
}
if (path.endsWith(`/admin/orders/${ADMIN_ORDER_ID}`)) return route.fulfill(ok(order(shipped)))
```

**The csrf-cookie gotcha:** the `apiClient` does CSRF fail-fast — it reads the non-httpOnly `csrf` cookie and echoes it as a header, **throwing if absent**. A hermetic test must **seed one** or every POST silently never fires:

```ts
await page.context().addCookies([{ name: 'csrf', value: 'e2e-csrf', url: 'http://localhost:5173' }])
```

**Assert a persistent signal, not a toast.** The tests assert on durable state the operation *produced* — the detail refetches and the status badge flips to **`Fulfilled`** (`await expect(page.getByText('Fulfilled')).toBeVisible()`), or a create navigates to `/admin/products/new-prod-1`. A flash message is racy; a badge that survives a refetch proves the round-trip.

**Resume claim:** "Hermetic Playwright E2E (zero backend in CI) via route mocking, with golden-path flows asserting persistent post-mutation state, plus `@axe-core` a11y gates."

### 8. The enforced coverage gate

**The pattern:** CI doesn't just *collect* coverage, it **fails the build under a threshold**. The `dotnet test --collect:"XPlat Code Coverage"` run emits per-project cobertura; `reportgenerator` merges them; an `awk` check gates the merged line number:

```bash
line=$(grep -oP 'Line coverage:\s*\K[0-9.]+' ./TestResults/coverage/Summary.txt)
awk -v c="$line" 'BEGIN { exit (c + 0 >= 85 ? 0 : 1) }' \
  || { echo "::error::Line coverage ${line}% is below the 85% gate"; exit 1; }
```

**Why it matters:** reported-but-ungated coverage is decoration — a number nobody is forced to respect, that quietly erodes. **Gated** coverage (currently ~95%, floor 85%) makes a coverage regression a *red build*, wired into branch protection. Branch coverage is reported, not gated — an honest distinction (branch targets are noisier).

**Resume claim:** "85% line-coverage gate enforced in CI via ReportGenerator-merged Coverlet output — a coverage drop fails the merge, not just a dashboard."

### 9. "Compose, not invent" — the thin hand-built `DataTable`

**The pattern:** every admin list (users, orders, …) renders through one ~60-line generic `DataTable<T>` over a **column-descriptor** API — `Column<T>` carries a `key`, a `header`, and a `cell: (row: T) => ReactNode` renderer; the **parent owns data fetching, paging, and filtering**:

```ts
export interface Column<T> { key: string; header: ReactNode; cell: (row: T) => ReactNode; className?: string }
```

**Why it matters / interview honesty:** the scope doc framed this as "compose, not invent," and the *real* code is even leaner than the plan — it's a plain `<table>`/`<thead>`/`<tbody>` primitive with **no heavyweight table dependency at all** (no `@tanstack/react-table` in `package.json`). Deliberately minimal — no built-in sort or virtualisation — so it stays a reusable building block the pages compose rather than each hand-rolling a `<table>`, and a11y (`scope="col"`, an `aria-label`) lives in one place.

**Resume claim:** "A single generic column-descriptor `DataTable` backs every admin grid — composition over a heavyweight table library, with table a11y centralized."

### 10. Runtime aggregation now, materialized view later

**The pattern:** the sales-by-day report loads the paid orders in range (with lines → category) and **aggregates in memory** with LINQ-to-objects `GroupBy`, rather than pushing a SQL `GROUP BY`:

```csharp
List<DailySalesDto> days = orders
    .GroupBy(o => DateOnly.FromDateTime(o.PlacedAt.UtcDateTime))
    .Select(group => new DailySalesDto(
        group.Key.ToString("yyyy-MM-dd"), group.Count(), group.Sum(o => (long)o.TotalCents)))
    .ToList();
```

**Why it matters — the honest scale story (the interview gold):** at portfolio scale this is trivially fast *and* sidesteps EF's awkward date-grouping translation; the code names its own ceiling — **"a SQL `GROUP BY` / indexed report view is the Phase-10 optimisation if k6 ever shows a hot path."** That's the defensible position: a deliberate, documented, measurement-gated choice — not a premature materialized view, and not an accidental table scan you can't account for. Money sums are widened to `long` (cents) before aggregation, and a soft-deleted product resolves to `"(uncategorised)"` via the global query filter.

**Resume claim:** "Sales reporting as in-memory aggregation with an explicit, k6-gated migration path to an indexed report view — scale decisions made by measurement, not guess."

---

I've written section §10 grounded in the actual Phase-3 source. Key files cited (all absolute paths under `src/api/Retail.Api/` and `src/web/`):
- `Common/Constants/Roles.cs` + `Program.cs` (lines 246–272) — named-policy RBAC matrix
- `Data/Interceptors/AuditTrailInterceptor.cs` + `Common/Abstractions/IAuditWriter.cs` / `AuditWriter.cs` — dual-interceptor stamp-vs-append + PII redaction
- `Services/AdminInventoryService.cs`, `Services/OrderRefundService.cs`, `Services/AdminOrderService.cs`, `Repositories/OrderRepository.cs` (`TryClaimForRefundByIdAsync`) — set-based audit obligation + concurrency claim
- `Controllers/AdminUsersController.cs` (body-dependent `User.IsInRole` check)
- `src/web/src/lib/auth/roleSets.ts` — frontend capability mirror
- `src/web/components/ui/data-table.tsx` — hand-built table (no `@tanstack/react-table`)
- `src/api/Retail.Api/Services/ReportQueryService.cs` — in-memory aggregation
- `.github/workflows/ci.yml` — 85% coverage gate + hermetic Playwright
- `src/web/e2e/support/admin-mock.ts` / `mock-api.ts`, `e2e/admin.spec.ts` — Playwright route mocking + csrf seed + persistent-signal asserts

One correction baked into the output for accuracy: the scope doc's "compose, not invent" mentioned `@tanstack/react-table`, but the as-built `data-table.tsx` uses **no** table library (confirmed absent from `package.json`) — pattern #9 reflects the real code, not the plan.

---

## 11. What's next — Phase 4 preview

### Phase 4 — AI: Copy Gen + Sentiment (`PLAN.md:511`)

Phase 3 gave the store a **staff back office** — an order workbench, fulfillment, refunds, an audit trail, RBAC, and sales-by-day. Phase 4 is the project's **first AI surface**, and it bolts onto exactly the seams Phase 3 just hardened. `PLAN.md:511` scopes four pieces:

| What lands | Where it plugs into Phase 3 |
|---|---|
| **`CopyGenService` + admin "Suggest Description"** | A new admin action on the **product editor** Phase 3 already ships (the same screen that hosts the deferred inventory-adjust UI from §16.1 review L17). It reuses the `Administrator`/`StoreManager` **policy-based authorization** matrix — the AI mutation is just another policy-gated admin write, and its every call is a candidate `IAuditWriter` action through the **dual-interceptor audit trail** Phase 3 stood up. |
| **`Review` entity + customer-submit endpoint + product review list** | The first new aggregate since Phase 3's `Shipment`/`AuditLog` (`migration 0008`); it follows the same `IAuditableEntity` + byte-backed-enum + migration conventions, and the customer-submit path rides the existing `[AllowAnonymous]`/account-scoped controller patterns. |
| **Azure AI Language sentiment via `ReviewSentimentHostedService`** | A `BackgroundService` in the **exact mold of Phase 2's `CartExpirySweeper`** — singleton, scope-per-tick, `PeriodicTimer` on the injected `TimeProvider`, per-tick `try/catch`. (`PLAN.md` notes the anomaly variant of this moves to an Azure Function in Phase 8; the sentiment scorer stays in-process for now.) |
| **Admin sentiment summary tile + "Products Needing Attention" panel** | New tiles on the admin dashboard alongside Phase 3's **sales-by-day Recharts chart**, reading the same RBAC-guarded analytics surface (`Reports.View`-style policies). |

The through-line: Phase 3 deliberately built the **admin shell, the policy matrix, the audit interceptor, and the background-service pattern** as reusable infrastructure, so Phase 4's AI features are *new endpoints and one new entity on existing rails*, not new plumbing. The named AI-provider is **Anthropic Claude** for the Phase-5 chatbot (`PLAN.md §8a`), but Phase 4's copy-gen is provider-pluggable and the sentiment path is **Azure AI Language**, not an LLM.

### Carried-forward follow-ups (named in `PHASE_3_SCOPE.md §16 / §16.1`)

Phase 3 closed its high/medium review findings and reconciled the rest rather than building speculatively. The explicitly **deferred** items it leaves wired for a later phase:

- **Full user-management** — `§16`: invite tokens, enable/disable, password reset, and the **invite/notification email** are deferred; Phase 3 shipped only the *thin demo slice* (`§3.4`). The `Users.*` policies exist; the lifecycle around them does not yet.
- **Audit/report Export** — `§16`: the `*.Export` policies are **defined but unused** until an export button exists, which is precisely what makes the "Staff is read-only on audit/reports" nuance real. Lighting up Export is a self-contained later add.
- **Anomaly / risk-queue filter** — `§16`: a **no-op placeholder filter** on the workbench today; it becomes real in **Phase 5** when the order-anomaly Z-score `BackgroundService` (`PLAN.md:526`) populates the queue.
- **Multi-shipment per order** — `§16`: today a `Shipment` is `1:0..1`, enforced by `UX_Shipment_OrderId`; the clean future change is **dropping that unique index** to allow split shipments.
- **Partial / RMA refund** — `§3.3`/`§8`: refundable state was narrowed to **`Paid` only** (revised down from `{Paid, Fulfilled}`) because refunding a *shipped* order would restock goods already with the customer — that's a **return/RMA flow, deferred**. The Phase-2 `charge.refunded` partial path is likewise still skipped.
- **Sales aggregation: runtime → materialized** — `§3.5`: sales-by-day runs as a **runtime EF `GroupBy`** (no view, no table, no migration), chosen because portfolio order volume makes the `GROUP BY` trivially fast and Testcontainers-testable. A materialized view/table is **deliberately premature** — the documented escalation trigger is **Phase 10 k6** flagging it as a hot path, at which point the migration becomes its own perf talking point.
- **`Drawer` / `Checkbox` / `Tabs` primitives** — `§16.1` (review L13): **not built**, because no tabbed or drawer screen emerged and the admin forms use the accessible **native `Select`** + a single native checkbox. They're deferred until a screen genuinely needs them rather than shipped as unused dead code; the Job-B component-library bullet rests on the **13 built-and-used primitives** Phase 3 actually has.
- **Lifting `FilterPanel`** — `§16.1` (review L14): it stays in `features/storefront` (shared by `CatalogPage` + `AdminProductsPage`) because it's coupled to `useCategoriesQuery`. The generic lift into `components/ui` (categories passed as a prop) is **deferred until a third consumer needs it** — the current cross-feature import is a known, accepted trade-off.

### Where to look up things later

- **"What did Phase 3 build?"** → this file
- **"Why did Phase 3 decide X?"** → `docs/PHASE_3_SCOPE.md` (authoritative for the phase; the `§16.1` as-built reconciliation records the post-review deltas)
- **"What's Phase 4 / the rest of the roadmap?"** → `docs/PLAN.md §13` (Phased Delivery Plan, `:511` onward)
- **"What's the current task / where are we?"** → memory's `project_progress.md`
