# Phase 2 Recap — What You Built and Why

> A self-learning recap of every concept, file, and connection introduced in
> Phase 2 (Epic 2 — Cart & Orders, built as Chunks 0–4 plus the adversarial
> review + hardening pass). Read top to bottom the first time; later, use the
> table of contents to jump back to specific patterns. Companion to
> `phase0_recap.md` (the seams) and `phase1_recap.md` (the catalog + account).
> Phase 1 gave a shopper something to browse and an account to own; **Phase 2
> lets them buy.**

## Table of contents

1. [The big picture — what Phase 2 turned on](#1-the-big-picture)
2. [Chunk 0 — The data model (enums, aggregates, DB-enforced invariants)](#2-chunk-0--the-data-model)
3. [Chunk 1 — The cart (guest + member + merge-on-login)](#3-chunk-1--the-cart)
4. [Chunk 2 — Inventory reservation, optimistic concurrency, the sweeper](#4-chunk-2--inventory-reservation)
5. [Chunk 3 — Stripe hosted checkout, the webhook, order creation](#5-chunk-3--stripe-checkout)
6. [Chunk 4 — My Orders (list, detail, cancel) + the guest lookup](#6-chunk-4--my-orders)
7. [The frontend — cart, checkout, orders, the auth-boundary cache](#7-the-frontend)
8. [The review + hardening pass](#8-the-review--hardening-pass)
9. [File relationship maps](#9-file-relationship-maps)
10. [Patterns to remember (interview material)](#10-patterns-to-remember)
11. [What's next — Phase 3 preview](#11-whats-next)

---

## 1. The big picture

### What Phase 2 turned on

Phase 1 left a **read-only store**: a catalog you could browse, an account you could own, an empty cart icon with nowhere to go. Phase 2 makes the store **transact**. An anonymous *or* logged-in shopper fills a cart, hands off to **Stripe hosted Checkout**, pays on Stripe's own page, and an **Order is created by a signature-verified, idempotent webhook** while stock decrements under **optimistic concurrency**. Then they can view and cancel what they bought.

It was built as five chunks, each a buildable, testable increment:

| Chunk | What shipped |
|---|---|
| **0 Data model** | The project's first **persisted enums** (4 byte-backed status types → `tinyint`), 8 entities split into a Cart aggregate and an Order aggregate, the **guest-XOR-member** identity invariant as a DB `CHECK`, a `rowversion` token on `Order`, **JSON address snapshots**, the `Seq_OrderNumber` sequence, and the Stripe idempotency ledger. Migration `0004_orders`. |
| **1 Cart** | Guest carts (an `anon_cart_key` cookie) **and** member carts, with **lazy merge-on-login**; add/update/remove/clear lines; an **add-time price snapshot**; a single shared `['cart']` cache feeding the page and the header badge. |
| **2 Inventory & concurrency** | Two-phase stock (reserve → commit at payment) with `InventoryItem.RowVersion` **optimistic concurrency** → 409, and a `CartExpirySweeper` `BackgroundService`. The "two buyers race the last unit" integration test is the headline artifact. |
| **3 Checkout** | **Stripe hosted Checkout** (test mode, app never touches a card PAN); the `checkout-session` endpoint (reserve → Stripe session); a **CSRF-exempt, signature-verified, idempotent webhook** that creates the `Order`; `charge.refunded` → refund + restock. |
| **4 Order viewing** | The customer **My Orders** list / detail / cancel, an **account-scoped** read with an IDOR guard, and an **[AllowAnonymous] guest lookup** keyed by the unguessable Stripe session id. |

The "Phase 1 wired it → Phase 2 used it" thread made concrete:

| Phase 1 seam | Phase 2 use |
|---|---|
| `ApiResponse<T>` envelope + `PagedResult<T>` | Every cart/order endpoint returns it; `My Orders` rides `PagedResult<OrderSummaryDto>` |
| `ExceptionMiddleware` switch | Gains `OutOfStockException`→409 `INVENTORY_INSUFFICIENT`, `ConcurrencyException`→409 `CONCURRENCY_CONFLICT` |
| `IAuditableEntity` + `AuditingInterceptor` | Every cart/order entity implements it — and the **set-based `ExecuteUpdate` paths stamp audit fields by hand** because they bypass the interceptor |
| `InventoryItem.RowVersion` (modeled in 1.2, unused) | Now **consumed** by the reservation guard — the textbook "concurrency token baked in early" payoff |
| Signed double-submit `CsrfMiddleware` | Gains a **path allowlist** so the server-to-server Stripe webhook (which can't carry a CSRF token) is exempt |
| The `Csrf:Key` signing pattern | Reused to **HMAC-sign the guest order-access token** |
| `openapi-fetch` typed client | Real cart/checkout/order calls; the order layer reads `response.status` off it to build a status-bearing `ApiError` |

### The vertical slice — the shape the whole phase takes

```
React (TanStack Query): cart cache, checkout redirect, order-by-session poll
   │  apiClient (typed, CSRF header auto-attached, httpOnly JWT cookie)
   ▼
Controller         ← binds cookies → CartCaller, validates (422), wraps ApiResponse<T>.
   │                  Identity comes from the auth cookie + anon-cart cookie, NEVER the body.
   ▼
Service            ← every business rule + the multi-table TRANSACTION. Throws domain
   │                  exceptions (OutOfStock / Concurrency / Conflict / NotFound).
   ▼
Repository         ← EF: AsNoTracking reads, TRACKED cart/order writes, and the set-based
   │                  ExecuteUpdate that IS the optimistic-concurrency primitive.
   ▼
RetailDbContext    ← AuditingInterceptor on SaveChanges; soft-delete filters from Phase 1.
   ▼
SQL Server         ← money as int cents; byte→tinyint enums; rowversion; FILTERED unique
                     indexes + CHECK constraints as the last line of defense.
        ▲
        │  out-of-process
        ▼
Stripe hosted Checkout ──(pays)──> Stripe webhook ──> POST /payments/stripe/webhook
                                                       (HMAC-verified, idempotent, creates Order)
```

Two things make Phase 2 *feel* different from Phase 1 and are the real interview meat: **money moves**, so correctness is non-negotiable (no double-charge, no double-restock, no oversell, no leaked PII); and **the order is created out-of-band by a webhook that lags the browser**, so half the design is about reconciling an asynchronous, at-least-once, multi-actor system.

### The three product bets (and one piece of intellectual honesty)

`docs/PHASE_2_SCOPE.md` is the authoritative pre-build design doc for the phase, and it **explicitly outranks** `PLAN.md` / `REQUIREMENTS.md` / `DATABASE_DESIGN.md` where they disagree — the deltas are listed rather than silently absorbed (the prior docs are deliberately left un-rewritten for a later docs pass). Three decisions are load-bearing:

1. **Full guest checkout.** `Order.CustomerProfileId` becomes **nullable** and gains `Order.GuestEmail`, with a **`CustomerProfileId` XOR `GuestEmail`** identity invariant. This supersedes `DATABASE_DESIGN §3.11` (which had the profile FK `NOT NULL`). Rather than fabricate throwaway accounts, an order carries exactly one identity source — and the XOR is enforced at the DB by `CK_Order_Identity`, not left to convention. Guest order retrieval is made IDOR-safe by an unguessable bearer (the Stripe session id) and a `CustomerProfileId == null` filter.
2. **The webhook lands on the API controller now**, at the route-stable `POST /api/v1/payments/stripe/webhook` — *not* the Azure Function + Event Grid pipeline `PLAN.md` mandates "from day 1." Why: no Functions project exists, the Functions/EventGrid/ServiceBus Bicep modules are placeholder stubs deploying zero resources, and Event Grid has no local emulator, so a Function webhook couldn't be exercised through the existing `ApiFactory` integration harness. The route is chosen to be **route-stable**: Phase 8 moves only the *handler body* behind a Function without changing the public route. A pragmatic testability-over-purity call, made explicit so it doesn't read as an oversight.
3. **A `§16` "checkout hardening" cluster is deferred to Phase 8.** The MVP happy path is provably correct; every deferred issue requires the shopper to mutate the cart *after* clicking checkout, or a webhook delayed past the cart's 30-min expiry. The clean fix (a `CheckingOut` cart state + reservation-expiry release + a reconciliation job) is genuinely **event-driven reconciliation territory** and belongs with the Phase-8 Service Bus/Event Grid work. A loud-failure guard added now prevents corruption in the interim.

The honesty: a `§4` reconciliation table records the doc-vs-code drift — migration is `0004_orders` (not `0002`; `0002`=catalog, `0003`=profile); **`Seq_OrderNumber` was never actually created** despite the docs claiming catalog shipped it (0 `HasSequence` hits), so Phase 2 creates it — which prevented a real boot-time failure, because `Order.OrderNumber` defaults to `NEXT VALUE FOR` a sequence that didn't exist.

### Conventions locked this phase

- **Money is integer cents end-to-end** (cart → order → Stripe → ledger). Never `decimal`. Stripe itself bills in integer minor units, so there's no `*100` anywhere.
- **Flat 10% GST** (`GstRate = 0.10`), computed in `long` with `MidpointRounding.AwayFromZero`, billed to Stripe as **its own `"GST (10%)"` line item** so the charged total equals the stored order total cent-for-cent. **Zero shipping** in the MVP.
- **The project's first persisted enums** — byte-backed, **1-based**, `tinyint` with a real default-`1` (`CartStatus.Open=1`, `ReservationStatus.Active=1`, `OrderStatus.Pending=1`, `PaymentStatus.Created=1`). `0` is a poison sentinel for `default(enum)`.

### Why these choices matter for the resume

| Resume claim | The Phase 2 evidence |
|---|---|
| "Idempotent Stripe webhook (no duplicate orders on at-least-once redelivery)" | `StripeWebhookService` (`EventUtility.ConstructEvent` + record-after-success), `ProcessedStripeEvent` ledger, `UX_Payment_StripeSessionId` filtered-unique index |
| "Optimistic concurrency: two buyers race the last unit, exactly one wins" | `InventoryReservationRepository.TryReserveAsync` (RowVersion-guarded `ExecuteUpdate`), the real-SQL-Server `TwoCartsRaceForLastUnit` test |
| "Guest + member cart with merge-on-login" | `CartService.ResolveAsync` (lazy merge), `CartCaller`/`CartResult`, the two filtered-unique single-open-cart indexes |
| "Defense-in-depth: invariants in app code AND database constraints" | `CK_Order_Identity`, `CK_InventoryReservation_Owner`, `UX_Cart_Open*`, `UX_Payment_StripeSessionId` (migrations `0005`/`0007`) |
| "PCI-scope reduction via hosted Checkout (never handle a card PAN)" | `IStripeCheckoutGateway` + `StripeCheckoutGateway` (Stripe `SessionService.CreateAsync`) |
| "Concurrency-safe refunds — provider hit exactly once" | `OrderStatus.Refunding` transient claim, `TryClaimForRefundAsync`, deterministic `IdempotencyKey = "refund:{pi}"` |
| "Background service reclaiming abandoned-cart stock, deterministically testable" | `CartExpirySweeper` (`BackgroundService` + `PeriodicTimer` on injected `TimeProvider` + scope-per-tick) |
| "Adversarial multi-dimension review, 0 critical/high, disciplined fix-vs-defer" | The `7ab423d`/`90f4bee`/`a3a32a4`/`f0485d9`/`8510c7d` fixes; `PHASE_2_SCOPE.md §16` |

---

## 2. Chunk 0 — The data model

This is the schema foundation the whole phase stands on. It introduces the **enum convention**, the two aggregates (Cart, Order), and — the part interviewers dig into — the **business invariants pushed down into SQL Server** as `CHECK` constraints, filtered unique indexes, and a `rowversion` token.

### What is in the data model

```
src/api/Retail.Api/
├─ Common/Enums/CommerceStatuses.cs        ← 4 byte-backed lifecycle enums (the FIRST persisted enums)
├─ Domain/Entities/
│  ├─ Cart.cs / CartItem.cs                ← the Cart aggregate (ephemeral session state)
│  ├─ Order.cs                             ← the Order aggregate root (immutable history)
│  ├─ OrderLine.cs                         ← line with SKU/name/price SNAPSHOTS
│  ├─ OrderPriceBreakdown.cs               ← 1:1 subtotal/tax/shipping (voucher/loyalty = 0 until Phase 7)
│  ├─ OrderAddressSnapshot.cs              ← keyless JSON value object (no Id, no audit)
│  ├─ Payment.cs                           ← append-only money ledger (signed cents)
│  ├─ InventoryReservation.cs              ← a soft stock hold, cart-owned XOR order-owned
│  └─ ProcessedStripeEvent.cs              ← the webhook idempotency ledger (bigint identity)
├─ Data/Configurations/                    ← 8 IEntityTypeConfiguration<T> (the real DDL decisions)
├─ Data/Migrations/20260616021308_0004_orders.cs   ← materializes all of it + Seq_OrderNumber
└─ Data/RetailDbContext.cs                 ← +8 DbSets + HasSequence("Seq_OrderNumber")
```

### Per-file purpose

#### `Common/Enums/CommerceStatuses.cs` (resume-gold)

Four lifecycle enums — `CartStatus`, `ReservationStatus`, `OrderStatus`, `PaymentStatus` — and **the project's first persisted enums**. Every one is `: byte`:

```csharp
public enum OrderStatus : byte
{
    Pending = 1,
    Paid = 2,
    Fulfilled = 3,
    Cancelled = 4,
    Refunded = 5,
    Refunding = 6,
}
```

Three decisions, all interview-grade:

- **`: byte` → SQL `tinyint`.** A status has fewer than 255 values, so a byte (1 byte) instead of an `int` (4) shrinks the hot status columns and any index over them, and makes the C# type line up exactly with the `tinyint` the schema specifies. It's about storage *and* type-honesty, not micro-optimization theatre.
- **Explicit, 1-based values, never renumbered.** The numbers are persisted in the DB *and* leak into API JSON, so they are part of the contract — reordering members would silently re-map every stored row. Starting at `1` makes `default(enum) == 0` a **detectable poison sentinel** (a status-`0` row is corrupt, not "the first real state"), which is why every config also `HasDefaultValue(...=1)`.
- **`Refunding = 6` is appended, not inserted.** It's a transient claim state that sits *logically* between `Paid` and `Refunded`, but inserting it there would renumber `Refunded`/`Cancelled` in existing rows. Appended at the end → every stored value stays stable. (Its mechanics are in Chunk 4.)

#### `Domain/Entities/Order.cs` (resume-gold)

The aggregate root. The headline fields:

```csharp
public Guid? CustomerProfileId { get; set; }
public string? GuestEmail { get; set; }
// ...
public OrderAddressSnapshot ShippingAddress { get; set; } = new();
public OrderAddressSnapshot BillingAddress { get; set; } = new();
public byte[] RowVersion { get; set; } = Array.Empty<byte>();
```

Four design choices:

- **Nullable profile + nullable guest email.** This is what enables guest checkout. But nullable-on-both-sides alone would permit *both-null* or *both-set* rows — the invariant is only real because `CK_Order_Identity` enforces the XOR at the database (below).
- **Address/line/SKU/name/price are immutable snapshots, not FKs to live rows.** An order is **historical record**. Pointing at a live `Address` or `ProductVariant` would let a later edit or delete silently rewrite where a past order shipped or what it cost — and guests have *no* saved `Address` at all, so a snapshot is the only viable representation for them.
- **`byte[] RowVersion` via `IsRowVersion()`.** Two writers can race a *status transition* — the Stripe refund webhook vs a customer cancel. Optimistic concurrency means a stale update matches **0 rows** and surfaces as a 409 instead of clobbering the other writer's transition. **Interview gotcha:** the token guards transitions specifically, not arbitrary edits — that's the whole point.
- **`OrderNumber` is an `int` from `Seq_OrderNumber`, separate from the `Guid Id`.** `Id` is the random surrogate key; `OrderNumber` is the friendly, monotonic, gap-tolerant reference a customer quotes to support. It's **store-generated**, so you can't assign it from C# (below).

Money is `SubtotalCents`/`TaxCents`/`ShippingCents`/`TotalCents` — integer cents throughout.

#### `Data/Configurations/OrderConfiguration.cs` (resume-gold)

The richest config in the phase. Four things land here.

**The reserved-word table + the identity CHECK:**

```csharp
builder.ToTable("Order", table => table.HasCheckConstraint(
    "CK_Order_Identity",
    "([CustomerProfileId] IS NOT NULL AND [GuestEmail] IS NULL) OR ([CustomerProfileId] IS NULL AND [GuestEmail] IS NOT NULL)"));
```

`Order` is a SQL reserved word — EF brackets it as `[Order]`. And **`CK_Order_Identity` is the DB twin of the service-layer rule**: the constraint, not the C# nullability, is what actually guarantees the XOR. A bug or a future code path that forgets the check can't persist a both-null or both-set order.

**The sequence-backed number + the concurrency token:**

```csharp
builder.Property(o => o.OrderNumber)
    .HasDefaultValueSql("NEXT VALUE FOR Seq_OrderNumber");
builder.Property(o => o.RowVersion).IsRowVersion();
```

`HasDefaultValueSql` makes `OrderNumber` **store-generated**: EF omits it on `INSERT` and reads the assigned value back. The DB sequence guarantees uniqueness/monotonicity under concurrent inserts, which an app-side `MAX()+1` can't.

**The JSON snapshot — and why the `ValueComparer` is mandatory:**

```csharp
var comparer = new ValueComparer<OrderAddressSnapshot>(
    (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null)
              == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(StringComparison.Ordinal),
    // ...
```

Both addresses persist as a single `nvarchar(max)` JSON column via a `ValueConverter`. The `ValueComparer` is the non-obvious half: the value object has no key and is a reference type, so without the comparer EF's change tracker **can't detect an in-place mutation** and an edited address might never be saved. (This is the same `ValueConverter` + `ValueComparer` pairing the Phase 1 `ProductVariant.Options` dictionary used — for the same reason.) The `CustomerProfile` FK is `Restrict` — a profile must never be deleted out from under its order history.

#### `Domain/Entities/OrderAddressSnapshot.cs`

A `sealed`, keyless, tableless value object — no `Id`, no audit fields, *not* an `IAuditableEntity`. It only ever exists inside `Order`'s two JSON columns, so the `AuditingInterceptor` never touches it. Modeling the address as a frozen snapshot (rather than an FK to live `Address`) is the same reasoning behind `OrderLine`'s SKU/name/price snapshots — **both freeze history against later catalog/address edits.**

#### `Domain/Entities/InventoryReservation.cs` (resume-gold)

A soft stock hold placed at checkout-start, tied to **either** a cart (pre-payment) **or** an order (post-commit):

```csharp
public Guid? CartId { get; set; }
public Guid? OrderId { get; set; }
```

A hold's owner changes across its lifecycle: cart-bound before payment, order-bound after. The service sets `CartId` on insert and, at commit, **nulls `CartId` and sets `OrderId`** — re-homing the hold. The DB `CHECK` (`CK_InventoryReservation_Owner`) backstops the XOR. It carries a **15-minute TTL**, deliberately shorter than the cart's 30-minute lifetime, because stock is the scarce contended resource — releasing abandoned holds quickly returns inventory to the pool faster.

#### `Data/Configurations/InventoryReservationConfiguration.cs` (resume-gold)

Mirrors the Order identity CHECK and — critically — sets **`NoAction` on both parent FKs**:

```csharp
builder.HasOne<Cart>().WithMany().HasForeignKey(r => r.CartId).OnDelete(DeleteBehavior.NoAction);
builder.HasOne<Order>().WithMany().HasForeignKey(r => r.OrderId).OnDelete(DeleteBehavior.NoAction);
```

**Interview gotcha:** a reservation is reachable from *two* parents (Cart and Order). Cascading from both would create a **multiple-cascade-path** that SQL Server rejects at migration time ("may cause cycles or multiple cascade paths"). `NoAction` is the canonical fix — and it's also semantically correct, because reservations are released/committed *explicitly* by the service, never cascade-deleted. The `InventoryItem` FK is `Restrict` (never hard-delete a stock row that still has holds drawing against it).

#### `Domain/Entities/ProcessedStripeEvent.cs` (resume-gold)

The webhook idempotency ledger — the **one entity in the phase that is neither `IAuditableEntity` nor `Guid`-keyed**:

```csharp
public class ProcessedStripeEvent
{
    public long Id { get; set; }
    public string StripeEventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
}
```

Both departures are deliberate. A **bigint `IDENTITY` PK** (not a random `Guid`) is a narrow, monotonic clustered key — for an ever-growing append-only log, that avoids the page-split / fragmentation cost a random GUID clustered key would incur. And it's **not** an `IAuditableEntity` because a write-once technical table has no "who updated" story. Idempotency comes from a **`UNIQUE` index on `StripeEventId` + insert** — atomic, race-free — not a `SELECT`-then-`INSERT` (which would race under concurrent redelivery).

#### `Data/Configurations/PaymentConfiguration.cs` (resume-gold)

Two things matter. The **filtered-unique session index** that is the DB idempotency guard for order creation:

```csharp
builder.HasIndex(p => p.StripeSessionId, "UX_Payment_StripeSessionId")
    .IsUnique()
    .HasFilter("[StripeSessionId] IS NOT NULL");
```

A concurrent webhook redelivery that tries to create a second `Payment` for the same session hits this index; `OrderCreationService` reads the violation as "already processed." The `IS NOT NULL` filter is mandatory because **refund rows carry a `NULL` `StripeSessionId`**, and SQL Server treats multiple `NULL`s as duplicate-violating in a plain unique index. `AmountCents` is a **signed `int`** (positive charge, negative refund) — the payment history is a ledger, and signed amounts sum to the net captured. `Currency` is `char(3)` via `IsFixedLength()` (canonical `AUD`).

#### `Data/Configurations/CartConfiguration.cs` (resume-gold)

The two **single-open-cart** filtered-unique indexes:

```csharp
builder.HasIndex(c => c.CustomerProfileId, "UX_Cart_OpenPerProfile")
    .IsUnique()
    .HasFilter("[Status] = 1 AND [CustomerProfileId] IS NOT NULL");
builder.HasIndex(c => c.AnonymousKey, "UX_Cart_OpenPerAnonymousKey")
    .IsUnique()
    .HasFilter("[Status] = 1 AND [AnonymousKey] IS NOT NULL");
```

These promote the app-level fetch-or-create invariant to a DB guarantee (full mechanics in Chunk 1). The filter does two jobs: `[Status] = 1` constrains only **Open** carts (Converted/Abandoned ones accumulate freely as history), and `IS NOT NULL` lets member and guest carts share the table without colliding on each other's `NULL` side. **Interview gotcha:** the filter spells `Open` as the literal `1` — SQL has no knowledge of `CartStatus.Open`, so reordering the enum would silently break it. **Honesty note (below):** these UX indexes actually arrived in migration `0007`, not `0004`.

#### `Data/Migrations/20260616021308_0004_orders.cs` (resume-gold)

The migration that materializes the whole schema. The **sequence comes first**, because the `Order` table's default references it:

```csharp
migrationBuilder.CreateSequence<int>(name: "Seq_OrderNumber", startValue: 10001L);
// ...
OrderNumber = table.Column<int>(type: "int", nullable: false, defaultValueSql: "NEXT VALUE FOR Seq_OrderNumber"),
RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
```

Starting at `10001` yields plausible, non-tiny human-facing numbers from day one. **The honesty callout:** `0004` created only the **non-unique** `IX_Cart_*_Status` indexes and the reservation FKs as `NoAction`; the single-open-cart `UX_Cart_Open*` indexes and the two owner `CHECK` constraints landed *later* in migration `0007` (constraint hardening). The config files (which show the `UX_`/`CHECK` versions) are ahead of `0004` — don't read `0004` and conclude the single-open-cart guarantee shipped with the original table.

#### `Data/RetailDbContext.cs`

Adds the 8 DbSets and declares the sequence **in `OnModelCreating`, not a config class**:

```csharp
builder.HasSequence<int>("Seq_OrderNumber").StartsAt(10001).IncrementsBy(1);
```

**Why here?** A sequence is a *model-level database object*, not an entity-type mapping, so it has no `IEntityTypeConfiguration` home — `OrderConfiguration` then wires `Order.OrderNumber` to `NEXT VALUE FOR` it. (And `base.OnModelCreating(builder)` still comes first, or Identity's tables are half-configured — the Phase 0 rule.)

### Chunk 0 — what to know cold

1. **Four `: byte` enums → `tinyint`, 1-based, default `1`** — the values are a persisted *and* serialized contract, so `Refunding` was appended as `6`, never inserted.
2. **Guest-vs-member identity is `CustomerProfileId` XOR `GuestEmail`, guaranteed by `CK_Order_Identity`** — nullable-on-both-sides alone would allow both-null/both-set rows.
3. **`Order.RowVersion` (and `InventoryItem.RowVersion`) are the only two rowversioned tables** — they guard status transitions / stock bumps so a stale write becomes a 409, not a clobber.
4. **Snapshots everywhere** — `OrderAddressSnapshot` (JSON value object, needs a `ValueComparer`) and `OrderLine`'s SKU/name/price columns freeze history against later catalog/address edits.
5. **`Seq_OrderNumber` (`StartsAt 10001`) is store-generated** via `HasDefaultValueSql` — it had to be *created* this phase because it never actually shipped despite the docs.
6. **`InventoryReservation`'s parent FKs are `NoAction`** to dodge SQL Server's multiple-cascade-path error (a row reachable from two parents); `ProcessedStripeEvent` is a non-audited bigint-identity append-only ledger.
7. **Idempotency is DB-enforced and double-layered** — `UX_ProcessedStripeEvent_StripeEventId` (per event) and the filtered `UX_Payment_StripeSessionId` (per session).

---

## 3. Chunk 1 — The cart

Story 2.1: a cart that works for an anonymous guest *and* a logged-in member, and folds the two together on login. The design centers on a single ownership model — a cart is owned by **either** a `CustomerProfileId` **or** an opaque `AnonymousKey`, never both — and the clean layering where the controller only translates HTTP + cookies and `CartService` owns every rule.

### What is in the cart slice

```
src/api/Retail.Api/
├─ Common/Constants/CartConstants.cs       ← the "anon_cart_key" cookie name (one source of truth)
├─ DTOs/Requests/  AddCartItemRequest, UpdateCartItemRequest
├─ Validators/     AddCartItemRequestValidator, UpdateCartItemRequestValidator (qty 1..99)
├─ DTOs/Responses/ CartDto (omits the anon key!), CartItemDto
├─ Mappers/CartMappers.cs                  ← explicit map; line total from the SNAPSHOT price
├─ Repositories/  ICartRepository / CartRepository   ← tracked reads, one shared Include graph
├─ Services/      ICartService (+ CartCaller, CartResult) / CartService   ← all the rules
└─ Controllers/CartController.cs           ← [AllowAnonymous], builds CartCaller, sets/deletes the cookie
```

### Per-file purpose

#### `Services/CartService.cs` (resume-gold)

The heart of the chunk. It resolves a `CartCaller` to a member or guest cart, snapshots the variant price at add-time, dedups lines by variant, performs the **lazy merge-on-login**, and maintains a 30-minute sliding expiry. Five things to know cold.

**1. Merge-on-login is LAZY** — it fires inside `ResolveAsync` on the *first cart touch after login* (any authenticated caller that still presents a guest cookie), **not** in the login endpoint:

```csharp
Cart? memberCart = await _repo.GetOpenCartByProfileAsync(profileId, ct);
Cart? guestCart = anonymousKey is { Length: > 0 }
    ? await _repo.GetOpenCartByAnonymousKeyAsync(anonymousKey, ct)
    : null;

bool hasGuestItems = guestCart is { Items.Count: > 0 };
if (memberCart is null && (create || hasGuestItems))
{
    memberCart = await CreateCartAsync(customerProfileId: profileId, anonymousKey: null, ct);
}

if (memberCart is not null && guestCart is not null && guestCart.Id != memberCart.Id)
{
    MergeInto(memberCart, guestCart);
}
```

**Why lazy?** It decouples the cart from the auth flow entirely — it works no matter *how* the user authenticated (password login, refresh-token rotation, a session resumed in a new tab). Putting merge logic in the login handler would miss carts accumulated between auth and the first cart interaction, and would couple two unrelated subsystems. **A common wrong answer is "merge on the login event."**

**2. The merge sums duplicate variants (capped) and abandons the guest cart:**

```csharp
if (line is null) { memberCart.Items.Add(new CartItem { /* ... */ Quantity = Math.Min(guestItem.Quantity, MaxLineQuantity), /* ... */ }); }
else { line.Quantity = Math.Min(line.Quantity + guestItem.Quantity, MaxLineQuantity); }
// ...
guestCart.Status = CartStatus.Abandoned;
```

The guest cart → `Abandoned` so it can never resurface as the user's open cart. The whole thing commits in **one `SaveChangesAsync`** so the merge (add lines + abandon) is atomic — a partial commit could double-count items or leave two open carts.

**3. The add-time price snapshot:**

```csharp
target.Items.Add(new CartItem { /* ... */ UnitPriceCentsSnapshot = variant.PriceCents }); // snapshot the live price now
```

`UnitPriceCentsSnapshot` freezes `variant.PriceCents` at add-time so the cart total stays stable even if an admin reprices the catalog mid-session. The honest tradeoff (documented in the code): this is a **UX-stability device, not the pricing authority** — checkout re-reads the live price and warns on drift.

**4. Dedup-by-variant.** Re-adding an existing variant *bumps* quantity (`Math.Min(..., 99)`) instead of inserting a second line. The service **must** bump, because the unique index `UX_CartItem_CartId_ProductVariantId` forbids a duplicate line at the DB level. The `99` cap appears in *three* places (both validators + the service's `Math.Min`) — the service clamp matters because a merge of two individually-valid carts could otherwise produce a 198-unit line.

**5. `GET` doesn't spawn a cart — but a member `GET` can still merge.** `EmptyCart()` returns a well-formed `CartDto` with `Guid.Empty` (a 200, never a 404) so the storefront can unconditionally render "your cart is empty." **Interview gotcha:** a member `GET` is *not* always read-only — if a lingering guest cookie carries items, `GetCartAsync` materializes the member cart, merges, and **persists** (which is why it calls `SaveChangesAsync` even though `GET` "shouldn't" write). `TimeProvider` is injected so the sliding expiry is deterministically testable.

#### `Services/ICartService.cs` (resume-gold) — `CartCaller` + `CartResult`

The two characteristic records of the chunk:

```csharp
public sealed record CartCaller(string? AppUserId, string? AnonymousKey);
public sealed record CartResult(CartDto Cart, string? AnonymousKey);
```

`CartCaller` carries **both** an optional user id and an optional anon key rather than a discriminated "member vs guest" type — because the merge case requires *both present at once* (an authenticated user who still has a guest cookie). A strict either/or model couldn't represent the exact state that triggers a merge. `CartResult` carries the cart **plus a cookie instruction**: a non-null `AnonymousKey` means "(re)write the guest cookie," `null` means "delete it." This is what lets the service drive cookie writes **without ever touching `HttpContext`** — keeping it host-agnostic and unit-testable. (`null` is overloaded to mean both "member cart" and "guest with no cart"; in both cases the controller deletes the cookie, which is correct for each.)

#### `Controllers/CartController.cs` (resume-gold)

A thin HTTP boundary. It builds the caller from the auth-cookie user id + the anon cookie, and obeys the returned instruction:

```csharp
private CartCaller BuildCaller() =>
    new(_currentUser.UserId, Request.Cookies[CartConstants.AnonymousCartKeyCookie]);
```

The cookie write is the load-bearing detail:

```csharp
Response.Cookies.Append(CartConstants.AnonymousCartKeyCookie, anonymousKey,
    new CookieOptions { HttpOnly = true, Secure = _secureCookies, SameSite = SameSiteMode.Lax, Path = "/", IsEssential = true });
```

**Interview gotcha — `SameSite=Lax`, not `Strict`.** The auth cookies are `Strict`; the anon-cart cookie is **`Lax` on purpose**, because a `Strict` cookie would *not* be sent on the **top-level navigation back from Stripe's hosted checkout** — orphaning the guest's cart at the worst possible moment. `HttpOnly` because no JS needs the key (the server resolves it), and the `Delete` call must repeat `Path`/`SameSite`/`Secure` or the browser treats it as a different cookie and won't clear it. The controller is `[AllowAnonymous]` but **state-changing calls still go through the global CSRF middleware** — open-to-everyone is not the same as unprotected, so the SPA fetches a CSRF token for guests too.

#### `Repositories/CartRepository.cs`

EF data access only. All lookups go through one private `OpenCartsWithGraph()` that filters `Status == Open` and eager-loads the whole `item → variant → (product + inventory)` graph, returning **tracked** entities (the service mutates them and relies on a single `SaveChanges`). `GetSellableVariantAsync` filters `IsActive && Product != null && Product.IsPublished` — and the global soft-delete query filter makes a deleted product's nav come back `null`, so **`Product != null` doubles as a soft-delete screen** (the service turns null into a 404 rather than leaking that the variant exists).

#### `Mappers/CartMappers.cs`

Explicit (no AutoMapper). Line total is computed from the **snapshot** price (`UnitPriceCentsSnapshot * Quantity`), reinforcing the stability contract at the read boundary. It derives an `InStock` hint from `Inventory.Available` — **but the digest and the code are both honest that this is a cheap UI hint, not a reservation.** Two users can both see `InStock=true` for the last unit; only the checkout reservation is authoritative. Treating this flag as a stock lock is the classic oversell bug.

#### `DTOs/Responses/CartDto.cs` + `CartConstants.cs`

`CartDto` carries `TotalQuantity` as a first-class field (backs the header badge without the client summing lines) and **deliberately omits the anonymous key** — the key lives *only* in the `HttpOnly` cookie, so JS/XSS can't read it or hijack another guest's cart. `CartConstants.AnonymousCartKeyCookie = "anon_cart_key"` is a named constant (read/write/delete across three sites; a typo would silently orphan guest carts) and it holds an opaque GUID, not the cart id or any user data.

### Chunk 1 — what to know cold

1. **Ownership is mutually exclusive** — a cart has *either* `CustomerProfileId` *or* `AnonymousKey`, with at-most-one-open-cart-per-owner enforced by the two filtered-unique indexes.
2. **Merge-on-login is lazy**, inside `ResolveAsync` on the first post-login cart touch — survives any auth path; guest cart → `Abandoned`; one atomic `SaveChanges`.
3. **Add-time price snapshot** keeps the cart total stable across repricing; checkout re-validates live. Money is integer cents.
4. **Re-adding a variant bumps quantity** (the unique `UX_CartItem` index forbids duplicate lines); the `99` cap lives in both validators *and* the service (merge can exceed it).
5. **`GET` doesn't create a cart** for a bare guest (returns `EmptyCart`/`Guid.Empty`/200), but a member `GET` can materialize + merge + persist a lingering guest cart.
6. **`CartCaller` (both identities) + `CartResult` (cart + cookie instruction)** keep all rules in the service and `HttpContext` out of it.
7. **`anon_cart_key` is `HttpOnly` + `SameSite=Lax`** (not `Strict`) so it survives the Stripe redirect return; `[AllowAnonymous]` does **not** exempt CSRF.

---

## 4. Chunk 2 — Inventory reservation

This is the **correctness spine** of the phase: it guarantees two shoppers can never both buy the same last unit. A reservation is a soft hold that inflates `InventoryItem.Reserved` (leaving `OnHand` untouched until payment commits in Chunk 3). The contended bump is guarded by SQL Server's `rowversion` via a **set-based `ExecuteUpdate` whose affected-row count is the race resolver**.

### What is in the reservation slice

```
src/api/Retail.Api/
├─ Exceptions/OutOfStockException.cs        ← → 409 INVENTORY_INSUFFICIENT
├─ Exceptions/ConcurrencyException.cs       ← → 409 CONCURRENCY_CONFLICT
├─ Middlewares/ExceptionMiddleware.cs       ← +the two 409 arms (and the 2601/2627 → CONFLICT arm)
├─ Services/IInventoryReservationService.cs / InventoryReservationService.cs   ← reserve/release rules + the tx
├─ Repositories/IInventoryReservationRepository.cs / InventoryReservationRepository.cs   ← the rowversion-guarded UPDATE
├─ Services/ICartSweepService.cs / CartSweepService.cs   ← one sweep pass (release + tombstone)
└─ HostedServices/CartExpirySweeper.cs      ← BackgroundService + PeriodicTimer on injected TimeProvider
```

### Per-file purpose

#### `Services/InventoryReservationService.cs` (resume-gold)

Reserve reads each line's `Available` + `RowVersion`, **fast-fails** on plainly-insufficient stock, then does a RowVersion-guarded bump whose affected-count it checks:

```csharp
if (stock.Available < quantity)
{
    throw new OutOfStockException($"Only {stock.Available} unit(s) available for variant '{item.ProductVariantId}'.");
}

int affected = await _repo.TryReserveAsync(stock.Id, stock.RowVersion, quantity, now, actor, ct);
if (affected == 0)
{
    throw new ConcurrencyException($"Stock for variant '{item.ProductVariantId}' changed during checkout. Please try again.");
}
```

The two-phase split is the point: the fast-fail handles the common *uncontended* insufficient-stock case with a precise, friendly 409 (`INVENTORY_INSUFFICIENT`) without ever touching the row; the guarded write handles the rare *race* where stock changed between read and write (`CONCURRENCY_CONFLICT`). **Interview gotcha:** the race is **not** resolved by EF throwing — `ExecuteUpdate` never raises on a 0-row `UPDATE`. You inspect the `int` return yourself; forgetting it means the loser silently "succeeds" having reserved nothing.

The whole cart is reserved in **one transaction**, and the operation is **idempotent**:

```csharp
IReadOnlyList<InventoryReservation> existing = await _repo.GetActiveCartReservationsAsync(cartId, ct);
if (existing.Count > 0) { return; }   // already held — double-click / refresh safe
// ...
await using var tx = await _db.Database.BeginTransactionAsync(ct);   // all-or-nothing across the cart
```

All-or-nothing: if line 3 can't be held, lines 1–2 must not stay reserved (or you leak phantom holds that silently shrink available stock). The service owns the transaction because the atomic unit is the business operation "reserve this cart." And **only `Reserved` moves on reserve — `OnHand` stays put until payment commits** (a reservation is a soft hold, not a sale; `Available = OnHand - Reserved` is the sellable figure). `actor` is `_currentUser.UserId`, **legitimately `null` for a guest** — the truthful audit value, not a placeholder.

#### `Repositories/InventoryReservationRepository.cs` (resume-gold)

The optimistic-concurrency primitive as a single atomic SQL `UPDATE`:

```csharp
await _db.InventoryItems
    .Where(i => i.Id == inventoryItemId && i.RowVersion == rowVersion)
    .ExecuteUpdateAsync(
        s => s
            .SetProperty(i => i.Reserved, i => i.Reserved + quantity)
            .SetProperty(i => i.UpdatedAt, now)
            .SetProperty(i => i.UpdatedBy, actor),
        ct);
```

SQL Server **re-stamps `rowversion` on every `UPDATE`**, so a racing writer (whose predicate carries the *old* rowversion) matches **0 rows**. Returning the count lets the caller distinguish "I won" (1) from "someone beat me" (0) **without locking or a `SELECT ... FOR UPDATE`**. The stock read is `AsNoTracking` (values only — there's no entity to track when the mutation is set-based). **Interview gotcha:** `ExecuteUpdate` bypasses the change tracker, so the `AuditingInterceptor` never sees it — that's why `UpdatedAt`/`UpdatedBy` are stamped by hand here (the same lesson as Phase 1's `ExecuteUpdate` default-clear). `Commit` decrements **both** `OnHand` and `Reserved` (the units leave the warehouse); `Restock` increments `OnHand`. Mixing these up breaks the `Available = OnHand - Reserved` invariant. Release deliberately has **no** RowVersion guard — each `Active` hold is released exactly once inside the transaction that flips it to `Released`, so double-release is structurally impossible; reserve is the only genuinely contended write.

#### `HostedServices/CartExpirySweeper.cs` (resume-gold)

A `BackgroundService` that ticks a `PeriodicTimer` and runs the sweep in a **fresh DI scope each tick**:

```csharp
using var timer = new PeriodicTimer(SweepInterval, _timeProvider);
while (await timer.WaitForNextTickAsync(stoppingToken))
{
    try
    {
        await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();
        ICartSweepService sweeper = scope.ServiceProvider.GetRequiredService<ICartSweepService>();
        await sweeper.SweepExpiredCartsAsync(stoppingToken);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Cart expiry sweep failed; will retry next interval.");
    }
}
```

Three interview-grade decisions:

- **Scope-per-tick (the captive-dependency fix).** A hosted service is a **singleton**; constructor-injecting a scoped, `DbContext`-backed service would create a *captive dependency* — one `DbContext` shared forever across ticks, not thread-safe, never disposed. `IServiceScopeFactory` + a per-tick scope gives each sweep its own `DbContext` with correct lifetime. Injecting `RetailDbContext` directly would *compile* but corrupt state at runtime.
- **`PeriodicTimer` on the injected `TimeProvider`.** A `FakeTimeProvider` lets a test advance virtual time to fire ticks and age carts past expiry — fast, flake-free timing tests — and unifies the clock so "expired" means the same thing to the sweeper and the data it sweeps. `WaitForNextTickAsync` doesn't drift (a `Task.Delay` loop does).
- **A per-tick `try/catch`.** A transient DB blip on one sweep must not tear down the loop for the life of the process — log and retry next interval. (Shutdown's `OperationCanceledException` is caught separately so it isn't logged as an error.)

**The three durations live side by side and are easy to conflate:** reservation TTL = **15 min**, cart lifetime = **30 min**, sweep cadence = **5 min**. They're independent — the sweeper just runs every 5 minutes and acts on whatever is past its expiry.

#### `Services/CartSweepService.cs`

One pass: select up to **200** oldest-expired Open carts, release each one's stock, then tombstone the cart guarded on **still-Open**:

```csharp
await _reservations.ReleaseCartReservationsAsync(cartId, ct);
await _db.Carts
    .Where(c => c.Id == cartId && c.Status == CartStatus.Open)
    .ExecuteUpdateAsync(
        s => s
            .SetProperty(c => c.Status, CartStatus.Abandoned)
            .SetProperty(c => c.UpdatedAt, now)
            .SetProperty(c => c.UpdatedBy, (string?)null),
        ct);
```

**The `Status == Open` guard is a TOCTOU defense:** if the cart *checked out* (converted) between the scan and the update, the guarded `UPDATE` matches 0 rows and the now-paid cart is left untouched — **you never abandon a cart that just paid**. Release-then-tombstone means stock is back in the pool before the cart leaves the Open set. The `BatchLimit` of 200 caps the work per pass so a backlog (e.g. after downtime) can't turn one sweep into an unbounded scan that starves the request path. **`UpdatedBy` is explicitly `null`** — a principal-less background pass; stamping `null` is the difference between an honest "machine did this" audit trail and one that misleadingly blames the last human.

#### `Middlewares/ExceptionMiddleware.cs` (resume-gold) — the new arms

```csharp
OutOfStockException =>
    (StatusCodes.Status409Conflict, "INVENTORY_INSUFFICIENT", ex.Message),

ConcurrencyException =>
    (StatusCodes.Status409Conflict, "CONCURRENCY_CONFLICT", ex.Message),

DbUpdateConcurrencyException =>
    (StatusCodes.Status409Conflict, "CONCURRENCY_CONFLICT",
     "The record was modified by another user. Please refresh and try again."),

DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } =>
    (StatusCodes.Status409Conflict, "CONFLICT",
     "That action conflicted with a concurrent change. Please try again."),
```

The contract is what lets the client treat reservation outcomes correctly: insufficient stock and a lost race are both 409s with **distinct codes**, so the UI can say "out of stock" vs "please retry." Note **two different mechanisms map to the same `CONCURRENCY_CONFLICT`** — our hand-rolled `ConcurrencyException` (we counted 0 rows from `ExecuteUpdate`) and EF's `DbUpdateConcurrencyException` (a tracked `SaveChanges` threw) — so the client handles both identically. And a unique-violation `2601/2627` (two concurrent first-adds racing the single-open-cart index) is a **retryable 409, not a 500** — the winner exists, the loser just retries.

#### The headline test: `InventoryReservationTests.TwoCartsRaceForLastUnit_ExactlyOneReservationWins` (resume-gold)

```csharp
Exception?[] outcomes = await Task.WhenAll(TryReserveAsync(cartA), TryReserveAsync(cartB));

Assert.Equal(1, outcomes.Count(o => o is null));                      // exactly one winner
Assert.Contains(outcomes, o => o is OutOfStockException or ConcurrencyException);
(_, int reserved) = await ReadStockAsync(variantId);
Assert.Equal(1, reserved);                                            // only the one unit is held
```

The two reserves run via `Task.WhenAll`, **each in its own `IServiceScope`** (own `DbContext`, own connection) against a variant seeded with `onHand: 1`. Optimistic concurrency only manifests when two genuinely separate connections contend at the engine — sharing a `DbContext` would serialize them and prove nothing. It runs against **real SQL Server, not SQLite** (SQLite doesn't implement `rowversion` the way SQL Server does). The loser assertion accepts **either** exception, because depending on interleaving the loser can fail at the fast-fail read (sees `Available 0` → `OutOfStock`) *or* at the guarded write (RowVersion moved → `Concurrency`). Asserting `reserved == 1` — not just "one exception thrown" — is the real proof that no double-hold leaked through.

### Chunk 2 — what to know cold

1. **The last-unit race is resolved by `Where(Id == id && RowVersion == read).ExecuteUpdate(Reserved += qty)`** — SQL Server re-stamps `rowversion` on `UPDATE`, so a racing writer matches **0 rows**. EF doesn't throw; you check the count and raise `ConcurrencyException`.
2. **Two distinct 409s** — an uncontended insufficient-stock read → `INVENTORY_INSUFFICIENT`; losing the RowVersion guard → `CONCURRENCY_CONFLICT`. A race loser can legitimately get either.
3. **The whole cart reserves in one transaction** (all-or-nothing) and reserve is **idempotent** (early-return if holds already exist); only `Reserved` moves until payment commits.
4. **Set-based `ExecuteUpdate` bypasses the `AuditingInterceptor`** — every statement stamps `UpdatedAt`/`UpdatedBy` by hand; the sweeper stamps `UpdatedBy = null` (machine actor).
5. **Three independent durations** — reservation TTL 15 min, cart lifetime 30 min, sweep cadence 5 min.
6. **`CartExpirySweeper` is a singleton `BackgroundService`** using `PeriodicTimer` on an injected `TimeProvider`, resolving the scoped sweep service from a **fresh DI scope per tick** (captive-dependency avoidance), with a per-tick `try/catch`.
7. **The sweep releases stock then tombstones with a `Status == Open` guard** (TOCTOU defense against a cart that paid mid-scan), bounded at 200 carts/pass.

---

## 5. Chunk 3 — Stripe checkout

The payment spine — split into a **"start" half** (`CheckoutService`: reserve → create Stripe session → return a redirect URL), a **webhook** (the inbound server-to-server call), and a **"complete" half** (`OrderCreationService`: idempotently turn a paid session into an `Order`). The app never touches a card PAN.

### What is in the checkout/payments slice

```
src/api/Retail.Api/
├─ Payments/
│  ├─ StripeOptions.cs                      ← SecretKey + WebhookSigningSecret (validated outside Dev only)
│  ├─ IStripeCheckoutGateway.cs / StripeCheckoutGateway.cs   ← hosted-session creation (lazy StripeClient)
│  ├─ IStripeWebhookService.cs / StripeWebhookService.cs     ← verify + dispatch + record-after-success
│  ├─ IProcessedStripeEventStore.cs / ProcessedStripeEventStore.cs   ← the dedup ledger
│  └─ IStripeRefundGateway.cs / StripeRefundGateway.cs       ← outbound refund (deterministic idempotency key)
├─ Services/
│  ├─ ICheckoutService.cs / CheckoutService.cs               ← the "start" orchestration + overflow guard
│  ├─ IOrderCreationService.cs / OrderCreationService.cs     ← the transactional, idempotent "commit"
│  └─ IOrderRefundService.cs / OrderRefundService.cs         ← shared idempotent refund+restock reversal
├─ Controllers/
│  ├─ OrdersController.cs (checkout-session)   ← [AllowAnonymous]; cart identity from cookies
│  └─ PaymentsController.cs (the webhook)       ← [AllowAnonymous] + CSRF-exempt; raw-body read
├─ Middlewares/CsrfMiddleware.cs               ← +the StripeWebhookPath allowlist
└─ Data/Migrations/20260616050844_0005_checkout_idempotency.cs   ← UX_Payment_StripeSessionId + CK_Order_Identity
```

### Per-file purpose — the "start" half

#### `Payments/StripeOptions.cs` + the gateway

Two secrets, modeled separately: `SecretKey` (`sk_test_…`, authenticates outbound session creation) and `WebhookSigningSecret` (`whsec_…`, verifies inbound events offline). They guard **two different trust boundaries**, so conflating them would let one missing secret mask the other. They're **not** validated in Development (a fresh clone with no keys must still run the catalog + cart; tests fake the gateway) but **are** required at boot outside Development — the `Validate(...).ValidateOnStart()` policy lives in `Program.cs`, gated on `!IsDevelopment()`, so a blank secret fails loudly at deploy time, not at the first real event.

`StripeCheckoutGateway` builds its client **lazily**:

```csharp
private readonly Lazy<IStripeClient> _stripe;

public StripeCheckoutGateway(IOptions<StripeOptions> options)
{
    _stripe = new Lazy<IStripeClient>(() => new StripeClient(options.Value.SecretKey));
}
```

**Interview gotcha:** Stripe.net's `StripeClient` ctor *rejects an empty key*. Built eagerly, it would break **every action** on any controller that injects the gateway — even read-only order endpoints that never call Stripe — on a clone with no key. `Lazy<>` defers the failure to a real checkout call (this is the `3e0f556` "lazy-Stripe" fix; the refund gateway does the same). The session is `Mode = "payment"`, `BillingAddressCollection = "required"` (Stripe collects the address on its hosted page → read back from `session.customer_details.address`), and the cart + identity ride in `Metadata`. Currency is forced lowercase (`ToLowerInvariant()`) because Stripe requires lowercase ISO codes.

#### `Services/CheckoutService.cs` (resume-gold)

The "start" orchestration. The **overflow guard runs before reserving or charging**:

```csharp
long subtotalLong = cart.Items.Sum(item => (long)item.UnitPriceCents * item.Quantity);
long taxLong = (long)Math.Round(subtotalLong * GstRate, MidpointRounding.AwayFromZero);
if (subtotalLong + taxLong > int.MaxValue)
{
    throw new ConflictException("Order total exceeds the maximum supported amount.");
}
```

**Ordering is the whole point:** an oversized cart must be rejected *here*, not charged at Stripe and *then* rejected at order creation — which would take the customer's money without producing an order (this is the **C4** fix). The math is `long`, computed from line items (not `cart.SubtotalCents`, an `int` that could already have wrapped). Tax is billed as **its own `"GST (10%)"` line** so Stripe's charged total equals the order total cent-for-cent:

```csharp
await _reservations.ReserveCartAsync(cartId, ct);
// ...
if (taxCents > 0) { lineItems.Add(new CheckoutLineItem("GST (10%)", taxCents, 1)); }
```

Member vs guest is branched on `caller.AppUserId` — members get `customerProfileId` in the metadata + a prefilled email; guests carry only `cartId`. The metadata **is** the channel by which the webhook learns which cart to finalize and whether to create a member or guest order. (`GstRate = 0.10` and `MidpointRounding.AwayFromZero` are duplicated in both `CheckoutService` and `OrderCreationService` — they **must** stay in sync or the Stripe-charged and stored totals diverge by a cent.)

#### `DTOs/Requests/StartCheckoutRequest.cs` + validator

```csharp
public sealed record StartCheckoutRequest(string ReturnBaseUrl);
```

The body carries **only** the return base URL — **the cart is identified by cookies, never the body.** Letting the client name the cart (or, worse, an amount) in the body is the classic e-commerce price-/cart-tampering hole; totals are recomputed server-side from snapshots. The validator requires an absolute `http(s)` URL (rejecting `javascript:`/`data:`/relative) and honestly notes a deferred hardening follow-up (check against the CORS allow-list — low risk because an open redirect here only sends the *paying user* to their *own* chosen URL).

### Per-file purpose — the webhook (inbound)

#### `Controllers/PaymentsController.cs` (resume-gold)

The single webhook endpoint. It reads the **raw body** and maps errors to a **retry contract with Stripe**:

```csharp
using var reader = new StreamReader(Request.Body);
string payload = await reader.ReadToEndAsync(ct);
string signature = Request.Headers["Stripe-Signature"].ToString();
```

**Why raw body, not a bound model?** The HMAC signature is computed over the *exact bytes* Stripe sent; any model-binding round-trip through `System.Text.Json` would re-serialize the payload and change the bytes, breaking verification. And the status codes are a **contract about retries**: `StripeException` → **400** (the payload is permanently unverifiable, so Stripe stops retrying a forged/garbage request); a genuine processing failure → **500** (Stripe retries later, which is what we want); success → **200**. Returning 200 on a processing failure would silently drop the event forever.

#### `Middlewares/CsrfMiddleware.cs` (resume-gold) — the webhook exemption

```csharp
// The Stripe webhook is a server-to-server POST that carries no cookies; it authenticates
// via its Stripe-Signature header (verified in the handler), not the SPA's double-submit
// token. So it is exempt from CSRF. Must match PaymentsController's webhook route.
private const string StripeWebhookPath = "/api/v1/payments/stripe/webhook";
// ...
if (!SafeMethods.Contains(context.Request.Method)
    && !context.Request.Path.StartsWithSegments(StripeWebhookPath))
{
    // ... double-submit cookie==header + signed-token validation, else 403
}
```

The Phase 1 `CsrfMiddleware` was method-only; Chunk 3 added a **path allowlist**. **Why is exempting it correct, not a hole?** A webhook is structurally **incapable** of passing double-submit CSRF: there's no browser, no cookie jar, and Stripe can't read our `csrf` cookie to echo it in a header — so the check would fail 100% of the time. CSRF defends requests a *browser* makes carrying ambient cookies; the webhook's defense is the **signature**. The exemption uses `StartsWithSegments` (segment-aware) so a path like `/payments/stripe/webhook-evil` does **not** match and stays protected. The string coupling between the constant and the controller route is documented so a future route change keeps both in sync.

#### `Payments/StripeWebhookService.cs` (resume-gold)

Verify-and-parse in one step, dispatch, then **record after success**:

```csharp
Event stripeEvent = EventUtility.ConstructEvent(
    payload, signatureHeader, _options.WebhookSigningSecret, throwOnApiVersionMismatch: false);

if (await _events.IsProcessedAsync(stripeEvent.Id, ct))
{
    _logger.LogInformation("Stripe event {EventId} already processed; skipping.", stripeEvent.Id);
    return;
}
// ... dispatch ...
await _events.RecordAsync(stripeEvent.Id, stripeEvent.Type, _timeProvider.GetUtcNow(), ct);
```

Three load-bearing decisions:

- **`ConstructEvent` verifies the HMAC *and* deserializes in one atomic step** — you cannot accidentally process an unverified payload, because parsing the event requires passing the signature check first. `throwOnApiVersionMismatch: false` is deliberate: a real event carries the Stripe **account's** pinned API version, which need not equal the SDK's compiled version; rejecting on mismatch would drop valid, correctly-signed events.
- **Record AFTER side effects, never before.** Stripe delivers at-least-once. Recording first then failing would mean the retry is skipped as "already processed" and the order/refund is **lost forever**. Recording after success means a failure leaves the event un-recorded so Stripe's retry safely re-runs it — **at-least-once delivery is converted to effectively-once by ordering**.
- **The `ProcessedStripeEvent` ledger is a fast-path skip, not the real guarantee.** It has a TOCTOU window (two redeliveries can both pass `IsProcessedAsync` before either records). The *hard* guarantees are the unique index on `Payment.StripeSessionId` (order creation) and `Order.RowVersion` (refund), which serialize racing writers at the database.

And the **partial-refund skip** — the classic Stripe gotcha:

```csharp
if (!charge.Refunded)
{
    _logger.LogWarning("charge.refunded {EventId} is a PARTIAL refund ...; skipping — partial refunds are not supported in Phase 2.", /* ... */);
    return;
}
await _orderRefund.RefundByPaymentIntentAsync(charge.PaymentIntentId, ct);
```

`charge.refunded` fires for **both** partial and full refunds. The discriminator is the boolean `Charge.Refunded` (full) vs `AmountRefunded < Amount` (partial), **not the event type**. Phase 2 supports full refunds only (the admin partial-refund UI is Phase 3), so a partial is logged and skipped — treating it as full would over-restock and write a ledger amount that doesn't match the money returned. (A sequence of partials that finally settles the full amount arrives with `Refunded == true` and is processed correctly then.)

#### `Payments/ProcessedStripeEventStore.cs` (resume-gold)

`AnyAsync` fast-path read, plus a best-effort insert that **swallows the unique-violation**:

```csharp
catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
{
    // A concurrent delivery recorded the same event id first ...
    // That's the idempotent outcome — detach our losing insert and move on.
    _db.Entry(record).State = EntityState.Detached;
}
```

Two concurrent redeliveries can both pass `IsProcessedAsync` and both try to insert the same `StripeEventId`; `UX_ProcessedStripeEvent_StripeEventId` makes one win and the loser's exception is the **expected idempotent outcome**, not an error. Detaching the losing entity matters — otherwise the failed insert stays tracked and could resurface on a later `SaveChanges`.

### Per-file purpose — the "complete" half + refund

#### `Services/OrderCreationService.cs` (resume-gold)

Idempotently and atomically turns a paid checkout into an `Order` graph. The **two-layer idempotency** is the headline. First the cheap read:

```csharp
Order? alreadyCreated = await _orders.GetByStripeSessionIdAsync(completion.StripeSessionId, ct);
if (alreadyCreated is not null) { return alreadyCreated; }
```

Then the **race-proof backstop** — the unique-index catch:

```csharp
_orders.AddOrder(order);
try { await _orders.SaveChangesAsync(ct); }
catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
{
    await tx.RollbackAsync(ct);
    return await _orders.GetByStripeSessionIdAsync(completion.StripeSessionId, ct)
        ?? throw new ConflictException("Order creation conflicted; please retry.");
}
```

The cheap read handles redeliveries; the unique index handles the genuine **concurrent** race the read can't (TOCTOU — two simultaneous webhooks both pass the read). Exactly one insert wins; the loser catches the dup-key, rolls back, and returns the winner's order. Removing the catch would let two simultaneous webhooks create two orders.

The whole finalization is **one transaction** — order-graph insert + reservation commits + cart flip:

```csharp
foreach (InventoryReservation reservation in active)
{
    await _reservations.CommitReservedAsync(reservation.InventoryItemId, reservation.Quantity, now, actor, ct);
    reservation.Status = ReservationStatus.Committed;
    reservation.OrderId = order.Id;
    reservation.CartId = null;          // now owned by the order, not the cart
}
cart.Status = CartStatus.Converted;     // tombstone the cart
```

Other decisions worth knowing: **totals are recomputed from the cart's price snapshots, not Stripe's reported amount** (the stored order is internally consistent and tamper-proof). The order graph is saved **first** so `order.Id` and the DB-assigned `OrderNumber` exist before reservations are re-homed onto it. `OrderStatus` is set directly to `Paid` (by webhook time, Stripe has confirmed payment — the order is *born* paid). And **zero active holds fails loudly** (rollback, no order) rather than silently underselling:

```csharp
if (active.Count == 0)
{
    throw new ConflictException($"Reservations for cart '{completion.CartId}' are no longer held; the order cannot be finalised.");
}
```

The holds may have been swept after a long webhook delay — failing loudly lets Stripe retry and a Phase-8 reconciliation job re-acquire or refund. (The honest residual gap, documented in `§16`.)

`CheckoutCompletion` — the record the webhook hands in — is **provider-agnostic** (no Stripe types), so the order-creation core could be driven by any provider or a test harness, and the member-XOR-guest invariant is stated in the contract.

#### `Services/OrderRefundService.cs` (resume-gold)

The **shared** idempotent reversal, called by *both* the `charge.refunded` webhook and the customer-cancel flow:

```csharp
if (order.Status == OrderStatus.Refunded) { return; } // idempotent — already reversed (e.g. cancel applied it before this webhook)
// ...
await using var tx = await _db.Database.BeginTransactionAsync(ct);
foreach (OrderLine line in order.Lines)
    await _inventory.RestockByVariantAsync(line.ProductVariantId, line.Quantity, now, actor, ct);
order.Status = OrderStatus.Refunded;
order.Payments.Add(new Payment { OrderId = order.Id, Provider = "stripe",
    StripePaymentIntentId = paymentIntentId, AmountCents = -order.TotalCents,
    Currency = "AUD", Status = PaymentStatus.Refunded });
await _orders.SaveChangesAsync(ct);
await tx.CommitAsync(ct);
```

The `already-Refunded` check is domain-level idempotency (the same `charge.refunded` can be redelivered, and a customer-cancel may have already flipped the order). Restock + status flip + negative `Payment` are **one transaction**, and `Order.RowVersion` serializes two concurrent appliers (cancel vs webhook) — the stale writer affects 0 rows → 409, never double-restocking. The refund is recorded as a **negative-`AmountCents` `Payment`** (an append-only ledger; `net = SUM(AmountCents)`), never by mutating the original charge row. `RestockByVariantAsync` is itself a set-based atomic `ExecuteUpdate` (`OnHand += qty`).

#### `Payments/StripeRefundGateway.cs` (resume-gold)

The outbound refund (used by customer-cancel), behind the same lazy client, with a **deterministic idempotency key**:

```csharp
await refundService.CreateAsync(
    new RefundCreateOptions { PaymentIntent = paymentIntentId },
    new RequestOptions { IdempotencyKey = $"refund:{paymentIntentId}" },
    ct);
```

Deriving the key from the PaymentIntent makes **Stripe itself collapse duplicate requests to one actual refund** — defense-in-depth behind the `Paid → Refunding` claim, so even if our claim races, the customer is never double-refunded. (This outbound idempotency is distinct from the *inbound* webhook idempotency.)

#### `Data/Migrations/20260616050844_0005_checkout_idempotency.cs` (resume-gold)

Turns the non-unique session index into the filtered-unique backstop and adds the identity CHECK:

```csharp
migrationBuilder.CreateIndex(name: "UX_Payment_StripeSessionId", table: "Payment",
    column: "StripeSessionId", unique: true, filter: "[StripeSessionId] IS NOT NULL");

migrationBuilder.AddCheckConstraint(name: "CK_Order_Identity", table: "Order",
    sql: "([CustomerProfileId] IS NOT NULL AND [GuestEmail] IS NULL) OR ([CustomerProfileId] IS NULL AND [GuestEmail] IS NOT NULL)");
```

**This index — not the `ProcessedStripeEvent` ledger — is what actually prevents two orders from one paid session under concurrent redelivery.** The `IS NOT NULL` filter is required because refund/non-session payments have a `NULL` session id and SQL Server would otherwise treat the multiple `NULL`s as duplicate-violating.

### Chunk 3 — what to know cold

1. **Hosted Checkout keeps the app out of PCI scope** — Stripe collects the PAN + address on its own page; the app only holds a session id + URL. `IStripeCheckoutGateway` is the seam that makes integration tests hermetic.
2. **The `StripeClient` is built lazily** — the ctor rejects an empty key, so eager build would break every endpoint on any controller injecting the gateway when no key is set.
3. **The overflow guard runs in `long`, before reserve/charge** — an oversized cart is rejected, never charged-then-rejected (C4). Totals come from snapshots, not Stripe's amount.
4. **The webhook reads the raw body** (signature is over exact bytes) and maps `StripeException`→400 / failure→500 / success→200 as a **retry contract** with Stripe.
5. **The webhook is `[AllowAnonymous]` + CSRF path-exempt** because it's server-to-server and structurally can't carry a CSRF token — its auth is the `Stripe-Signature` HMAC, verified by `EventUtility.ConstructEvent` (which verifies *and* parses atomically).
6. **Record-after-success** converts at-least-once delivery to effectively-once; the `ProcessedStripeEvent` ledger is a racy fast-path, the **DB unique index + `RowVersion` are the hard guarantees**.
7. **Order creation is one transaction + two-layer idempotency** (cheap read + `UX_Payment_StripeSessionId` dup-key catch); zero active holds **fails loudly** rather than overselling. `charge.refunded` **skips partials** (`if (!charge.Refunded) return;`).

---

## 6. Chunk 4 — My Orders

The read-and-cancel half: once Chunk 3 turns a paid session into an `Order`, Chunk 4 lets the buyer **see** it and (if paid) **undo** it. Four endpoints on `OrdersController`, and the hard parts are all about **not leaking data** and **not double-refunding**.

### What is in the My-Orders slice

```
src/api/Retail.Api/
├─ DTOs/Requests/OrderListQuery.cs          ← paged [FromQuery] DTO (Page/PageSize, defaults 1/20)
├─ DTOs/Responses/  OrderSummaryDto, OrderDetailDto, OrderLineDto, OrderAddressDto
├─ Mappers/OrderMappers.cs                  ← explicit map from purchase-time SNAPSHOTS; status as enum name
├─ Services/IOrderQueryService.cs / OrderQueryService.cs           ← profile-scoped reads, paging clamp
├─ Services/IOrderCancellationService.cs / OrderCancellationService.cs   ← the claim → Stripe → reversal flow
├─ Controllers/OrdersController.cs          ← list/detail/cancel (Customer-only) + by-session (anon)
└─ Repositories/OrderRepository.cs          ← the 3 read shapes + the atomic refund-claim/release pair
```

### Per-file purpose

#### `Controllers/OrdersController.cs` (resume-gold) — the auth split

List/detail/cancel are `[Authorize(Roles = Roles.Customer)]`; the guest lookup and checkout are `[AllowAnonymous]`:

```csharp
/// Guest order lookup by Stripe session id — the high-entropy bearer the success page holds.
/// Open to anyone with the session id; the id itself is the (unguessable) access token.
[HttpGet("by-session/{sessionId}")]
[AllowAnonymous]
public async Task<IActionResult> GetOrderBySession(string sessionId, CancellationToken ct)
{
    OrderDetailDto order = await _orders.GetOrderBySessionAsync(sessionId, ct);
    return Ok(ApiResponse<OrderDetailDto>.Ok(order));
}
```

The role split encodes **who the resource belongs to**, not just whether someone is logged in. A guest who checked out has no account, so their only handle on the order is the high-entropy Stripe session id from the success URL — that path *must* be anonymous or guests could never see what they bought. **Interview gotcha:** the by-session route is `[AllowAnonymous]` but is **not an open door** — its safety lives entirely in the repository's `CustomerProfileId == null` filter (below). Read the controller and repo *together* or you'll wrongly conclude it leaks member data. The cart/owner is always derived from the caller (auth cookie + anon-cart cookie), never the body, and `TryGetUserId` defensively re-checks the user id even though `[Authorize]` already guarantees a principal.

#### `Repositories/OrderRepository.cs` (resume-gold) — the security hinges

**The S1 fix** — the guest by-session lookup returns *ownerless* orders only:

```csharp
// Guest orders only. ... Member orders ALSO carry a Payment with this session id, so
// without the CustomerProfileId == null guard a member's PII-bearing order would be
// readable by any unauthenticated caller holding the id — members must instead use the
// account-scoped path (GetOwnedByIdAsync).
.FirstOrDefaultAsync(
    o => o.CustomerProfileId == null && o.Payments.Any(p => p.StripeSessionId == stripeSessionId), ct);
```

A member's order *also* carries a `Payment` row with the session id, so without the null-profile guard the `[AllowAnonymous]` endpoint would return a logged-in customer's PII-bearing order to anyone holding the (logged!) session id. **The whole anonymous-endpoint safety hinges on one clause** — drop it and you have a live PII leak even though nothing in the controller changed.

**The IDOR guard** — id *and* owner in one `WHERE`:

```csharp
await _db.Orders.AsNoTracking()
    .Include(o => o.Lines)
    .FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerProfileId == customerProfileId, ct);
```

An order that exists but belongs to someone else simply doesn't match — the service then 404s. Ownership is enforced **in the query**, not as a forgettable post-fetch `if`. (And `GetChargePaymentIntentIdAsync` filters `AmountCents > 0` to find the *charge*, because refunds are stored as negative-amount rows.)

**The C3 fix** — the atomic refund claim:

```csharp
int affected = await _db.Orders
    .Where(o => o.Id == orderId
             && o.CustomerProfileId == customerProfileId
             && o.Status == OrderStatus.Paid)
    .ExecuteUpdateAsync(
        s => s
            .SetProperty(o => o.Status, OrderStatus.Refunding)
            .SetProperty(o => o.UpdatedAt, now)
            .SetProperty(o => o.UpdatedBy, actor),
        ct);
return affected == 1;
```

SQL Server serializes the row `UPDATE`, so of two simultaneous cancels exactly one matches the still-`Paid` row (`affected == 1`, wins); the other matches 0 rows (status already `Refunding`, loses → 409). **This caps the Stripe refund call to once per order** — the same pattern as `TryReserveAsync`. The claim also scopes by `customerProfileId`, so it doubles as a second ownership check on the write path.

#### `Services/OrderCancellationService.cs` (resume-gold)

The state machine: ownership check → precondition → **claim before Stripe** → reversal, with rollback if Stripe fails:

```csharp
if (!await _orders.TryClaimForRefundAsync(orderId, profileId, now, appUserId, ct))
{
    throw new ConflictException($"Order #{order.OrderNumber} can't be cancelled in its current state.");
}
try { await _refundGateway.RefundAsync(paymentIntentId, ct); }
catch
{
    await _orders.ReleaseRefundClaimAsync(orderId, _timeProvider.GetUtcNow(), appUserId, ct);
    throw;
}
await _orderRefund.RefundByPaymentIntentAsync(paymentIntentId, ct);
```

**Why claim before the network call?** Without the pre-claim, two concurrent cancels both read `Paid`, both call Stripe, and the buyer is refunded twice. Claiming first means Stripe is hit only for the single claim winner; the loser short-circuits to 409 *before* touching the payment provider. If `RefundAsync` throws, `ReleaseRefundClaimAsync` reverts `Refunding → Paid` (scoped to still-`Refunding` rows) so the order stays cancellable, and the original exception is rethrown. The local reversal **reuses the exact same idempotent `OrderRefundService.RefundByPaymentIntentAsync`** the webhook uses — because Stripe will *also* fire `charge.refunded` for this refund, and the idempotent reversal makes that follow-up webhook a harmless no-op. **One reversal implementation, two triggers, no double-restock.** Only `OrderStatus.Paid` is cancellable; everything else throws `ConflictException`.

**Interview gotcha:** the initial `Status != Paid` read check is necessary but **not sufficient** for concurrency — it's a read, so two callers can both pass it. The `TryClaimForRefundAsync` set-based `UPDATE` is the real serialization point; the early check just gives a fast, friendly 409 in the common case.

#### `Services/OrderQueryService.cs`

Read-side. It **clamps paging in the service**, not the controller or DTO:

```csharp
int safePage = page < 1 ? 1 : page;
int safeSize = Math.Clamp(pageSize, 1, MaxPageSize);   // MaxPageSize = 100
Guid profileId = (await _profiles.GetMyProfileAsync(appUserId, ct)).Id;
(IReadOnlyList<Order> items, int total) = await _orders.GetPagedByProfileAsync(profileId, safePage, safeSize, ct);
```

Clamping at the **trust boundary** (the service) guarantees every caller — tests, future callers — gets sane bounds and caps a `pageSize=1000000` scrape. And every member method first resolves `profileId` via `GetMyProfileAsync(appUserId)` — because **orders are keyed by `CustomerProfileId`, not the Identity user id**, so resolving the profile and threading it into the scoped queries is what makes them account-scoped. Both read misses throw `NotFoundException` → uniform 404 (no branch could accidentally distinguish "not found" from "not yours").

#### `Mappers/OrderMappers.cs` + DTOs

Explicit (no AutoMapper), built from **snapshot fields** (`NameSnapshot`, `SkuSnapshot`, `UnitPriceCents`, the address snapshot) so an order renders its at-purchase state even if the product is later renamed, repriced, or deleted. **Interview gotcha:** status is serialized as `order.Status.ToString()` — the API exposes the enum **name** (`"Refunding"`), not the numeric value, so renaming an enum member is a breaking API change even though the stored `tinyint` is unchanged. `OrderListQuery` is `Page`/`PageSize` (PascalCase) with defaults `1`/`20` and **no clamping of its own** (the bound lives in the service) — a documented tradeoff, and the FE/BE casing footnote: the OpenAPI contract is PascalCase, ASP.NET binds case-insensitively, so the historically-lowercase `page`/`pageSize` the frontend sent both work.

### Chunk 4 — what to know cold

1. **Two read-path security guards** — the **IDOR guard** (`GetOwnedByIdAsync` filters id AND owner in one query; miss → 404 not 403, so an id's existence is never confirmed) and the **S1 guest-leak fix** (`CustomerProfileId == null` on the by-session lookup).
2. **The by-session endpoint is anonymous by design** — the Stripe session id is the high-entropy bearer; its safety lives in the repo's null-profile filter, not the controller.
3. **Concurrent-cancel correctness (C3)** — a transient `OrderStatus.Refunding = 6` claimed atomically (`Paid → Refunding`, `affected == 1` wins, loser 409) **before** the Stripe call caps the refund to once per order.
4. **Claim before the network call; revert on failure** — `ReleaseRefundClaimAsync` (`Refunding → Paid`) keeps a failed refund cancellable. Belt-and-suspenders with the gateway's `IdempotencyKey`.
5. **The local reversal is the shared `OrderRefundService`** — the same idempotent method the webhook calls, so cancel-then-webhook never double-restocks.
6. **Orders are scoped by `CustomerProfileId`, not the Identity user id** — the service bridges the two keys and clamps paging at that trust boundary.
7. **Read DTOs come from purchase-time snapshots; status is the enum *name*** — historical orders survive catalog changes, and renaming a status member is a breaking API change.

---

## 7. The frontend

The customer-facing half of the slice: the React 19 + TanStack Query UI that drives a guest/member cart, hands off to Stripe via a **full-page redirect**, confirms an order created by a webhook that **lags the browser**, and renders order history. The defining theme is **server-state correctness across a racy, multi-actor, out-of-process system** — and three review-fix lineages live here (C5, B4, C2).

### What is in the frontend slices

```
src/web/src/features/
├─ cart/
│  ├─ hooks/useCartQuery.ts        ← the single ['cart'] key, staleTime:0, shared by page + badge
│  ├─ hooks/useCartMutations.ts    ← add/update/remove/clear — cache WRITE-THROUGH via setQueryData
│  ├─ CartPage.tsx                 ← states + the single `busy` gate + the Stripe redirect
│  └─ components/CartLineItem.tsx, CartSummary.tsx
├─ checkout/
│  ├─ hooks/useStartCheckout.ts    ← POST checkout-session → returns Stripe URL (origin in, URL out)
│  └─ CheckoutSuccessPage.tsx      ← polls by-session; clears cart ONLY on terminal state (C5)
├─ orders/
│  ├─ hooks/useOrdersQuery.ts      ← the ApiError class (B4) + orderKeys + the retry-while-404 poll
│  ├─ hooks/useOrderMutations.ts   ← cancel: write detail + invalidate list
│  ├─ OrdersPage.tsx, OrderDetailPage.tsx (B4 consumer), components/OrderStatusBadge.tsx
└─ auth/useSessionActions.ts       ← invalidate ['cart'] on login, REMOVE ['cart']+['orders'] on logout (C2)
```

### Per-file purpose

#### `features/cart/hooks/useCartQuery.ts` + `useCartMutations.ts` (resume-gold)

One shared `['cart']` cache entry for the whole app — the cart page and the header badge both call `useCartQuery()`:

```ts
export const cartKey = ['cart'] as const
// the cart page and the header badge both call this, sharing one cache entry, so a mutation
// that writes the cache refreshes both at once.
```

A single per-caller cart is a single resource → one cache entry. It overrides the global 30s `staleTime` to `0` (carts mutate from other tabs and the checkout flow, so a stale window would show wrong counts). The four mutations use **cache write-through**, not invalidate-and-refetch:

```ts
onSuccess: (cart) => queryClient.setQueryData(cartKey, cart),
```

**Why write-through?** Every cart endpoint already returns the complete, server-authoritative cart, so a second GET would be a wasted round-trip. Writing the response into the cache updates the page *and* the badge in **one network call**, with the server as the source of truth. It's **not optimistic** (the cache updates in `onSuccess`, after the server responds, not in `onMutate`) — which avoids the rollback complexity of true optimistic UI; the page instead gates double-submits with a `busy` flag. `PUT` sets the **absolute** quantity (idempotent — retrying can't double-apply).

#### `features/cart/CartPage.tsx` + `CartLineItem` + `CartSummary`

A single derived `busy` flag disables every control while any mutation is in flight (the practical substitute for optimistic locking, given write-through has a window before the cache updates). The checkout CTA does a **full document navigation**, not React Router:

```tsx
function handleCheckout() {
  startCheckout.mutate(undefined, {
    onSuccess: (url) => window.location.assign(url),
    onError: (error) => notifyError(error instanceof Error ? error.message : 'Could not start checkout.'),
  })
}
```

Stripe hosted checkout lives on a **different origin** (`checkout.stripe.com`) — SPA client-side routing can't go there, so `window.location.assign` is the only correct hand-off (and it conveniently abandons the SPA's in-memory state exactly where ownership transfers to Stripe). The line-item stepper sends the **absolute target** quantity (matching the idempotent `PUT`), clamps at `1..99`, and disables the minus button at `1` so removal stays an explicit `DELETE` rather than decrementing to zero. `CartSummary` shows **only subtotal** with a "tax + shipping at checkout" disclaimer — tax/shipping are computed server-side in the Stripe session, so a fabricated client total would mismatch what's charged.

#### `features/checkout/hooks/useStartCheckout.ts` (resume-gold)

```ts
const { data, error } = await apiClient.POST('/api/v1/orders/checkout-session', {
  // The SPA's origin, so the backend can build the Stripe success/cancel return URLs.
  body: { returnBaseUrl: window.location.origin },
})
if (error || !data?.data?.url) { throw new Error('Failed to start checkout.') }
return data.data.url
```

Sending just `window.location.origin` lets the **backend** compose the absolute return URLs (the success URL must embed Stripe's `{CHECKOUT_SESSION_ID}` template, which only Stripe + the server can do) — working identically in dev (`localhost`) and prod (APIM origin) with zero environment-conditional code. The hook returns *only* the URL; the redirect is left to the caller (so the hook is unit-testable without a jsdom navigation stub). **Stock is reserved at session creation** (the POST), so a 409 means empty/out-of-stock — and abandoning Stripe leaves a reservation the sweeper later releases.

#### `features/checkout/CheckoutSuccessPage.tsx` (resume-gold) — the C5 fix

The landing page after Stripe redirects back. The order is materialized **out-of-band by the webhook, which lags the redirect**, so the page **polls** `useOrderBySessionQuery` (retry-while-404) and clears the cart **only on a terminal state**:

```tsx
// Clear the cart once we KNOW the order landed (or the poll gave up) — never on every poll tick.
useEffect(() => {
  if (order || isError) {
    void queryClient.invalidateQueries({ queryKey: cartKey })
  }
}, [order, isError, queryClient])
```

**This is the literal C5 fix:** clearing on every render/poll would empty the cart *before* the order is confirmed, so a webhook failure would lose the cart **and** have no order. Clearing only on terminal states (order landed OR poll exhausted) guarantees the cart survives until either an order exists or payment is known-good. On poll exhaustion it shows "Payment received / still finalising" (a **soft** fallback — payment already succeeded at Stripe, so a hard error would alarm a customer who was actually charged) and points them to My Orders. It uses `invalidateQueries(cartKey)` (the webhook already emptied the server cart; the client just drops its stale cache) and the clear lives in an **effect, not render** (invalidating during render is a React anti-pattern that would loop).

#### `features/orders/hooks/useOrdersQuery.ts` (resume-gold) — the B4 fix

The foundation is a custom error that **preserves the HTTP status**:

```ts
/** Error that preserves the HTTP status so the UI can tell 404 / 401 / network apart. */
export class ApiError extends Error {
  readonly status: number | undefined
  constructor(message: string, status: number | undefined) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}
```

A plain `Error` loses *why* a request failed, so the UI couldn't branch. Preserving `response?.status` (from openapi-fetch) lets retry policies skip definitively-failed requests:

```ts
retry: (count, err) =>
  err instanceof ApiError && (err.status === 404 || err.status === 401) ? false : count < 2,
```

A 404 (not yours) or 401 (signed out) will *never* succeed on retry; only network/5xx blips are worth retrying. And the by-session poll retries **only while it's still a 404** (the webhook hasn't run yet), capped:

```ts
// Keep re-polling only while it's still a 404 (webhook hasn't run); cap the wait.
retry: (count, err) => err instanceof ApiError && err.status === 404 && count < 10,
retryDelay: 1500,
```

10 attempts × 1.5s gives the webhook a ~15s budget before the success page shows its soft fallback. `orderKeys` is a structured factory (`all`/`list(page)`/`detail(id)`/`bySession`) so a cancel can blow away the whole namespace while individual entries stay cacheable. **Note:** `response` is `undefined` on a true network failure, so `response?.status` is `undefined` — the UI maps *that* to "check your connection," distinct from a real 404.

#### `features/orders/OrderDetailPage.tsx` (resume-gold) — the B4 consumer

```tsx
const status = error instanceof ApiError ? error.status : undefined
const message =
  status === 401 ? 'Please sign in to view this order.'
    : status === 404 ? 'Order not found.'
    : status === undefined ? 'Could not reach the server. Check your connection and try again.'
    : 'Something went wrong loading this order.'
```

Before B4, *every* failure read "Order not found" — wrong and alarming for a signed-out user or a server outage. The `undefined` branch (network) is ordered **after** the explicit codes so a real 404 isn't mis-mapped. The Cancel button only renders when `order.status === 'Paid'` (matching the server-side state machine), and after a successful cancel the write-through flips status off `Paid` so the button disappears with no manual refetch.

#### `features/orders/components/OrderStatusBadge.tsx`

A status→variant lookup table with a **safe neutral fallback**:

```tsx
const VARIANT: Record<string, BadgeVariant> = {
  Pending: 'warning', Paid: 'success', Fulfilled: 'default',
  Cancelled: 'secondary', Refunding: 'warning', Refunded: 'secondary',
}
export function OrderStatusBadge({ status }: { status: string }) {
  return <Badge variant={VARIANT[status] ?? 'secondary'}>{status}</Badge>
}
```

The `?? 'secondary'` fallback means a newly-added backend status renders safely (neutral) instead of crashing — forward-compatible by construction. The PascalCase enum **name** is the source of truth for both colour and label (no separate label map to drift).

#### `features/auth/useSessionActions.ts` (resume-gold) — the C2 fix

Centralizes auth-transition cache maintenance, and the **asymmetry is the whole point**:

```ts
signIn(dto: AuthUserDto | null | undefined) {
  applyAuthUser(dto)
  void queryClient.invalidateQueries({ queryKey: cartKey })
},
signOut() {
  applyAuthUser(null)
  queryClient.removeQueries({ queryKey: cartKey })
  queryClient.removeQueries({ queryKey: orderKeys.all })
},
```

On **login, invalidate** the cart (the server merged the guest cart into the member cart, so the cached count is stale but the cart still exists — refetch the merged truth). On **logout, remove** `['cart']` *and* `['orders']` outright (member-scoped data must vanish with **no refetch window** — on a shared device, the next anonymous viewer must not see, even briefly, the previous member's data). `invalidate` would refetch and could momentarily re-show cached rows; `removeQueries` drops them with zero stale window. It's a **hook** using `useQueryClient()` because the `QueryClient` is `useState`-scoped per `<AppProviders/>` (not a module singleton), and centralizing it makes the cache-bleed **structurally impossible**, not a discipline problem.

### The frontend — what to know cold

1. **One shared `['cart']` cache** feeds the page and the header badge, works for guests; mutations **write-through** (`setQueryData`) the authoritative cart, not invalidate-and-refetch.
2. **Checkout is a full-page hand-off** — `window.location.assign(url)` to Stripe's origin (SPA routing can't reach it); stock is reserved at the POST, so a 409 means empty/out-of-stock.
3. **The success page polls by-session (retry-while-404, ~10×1.5s)** and clears the cart **only on a terminal state** (C5) — a webhook failure can't lose both the cart and the order.
4. **`ApiError` preserves the HTTP status** (B4) so the UI branches 401 / 404 / network / generic, and retry policies skip definitive 404/401.
5. **Auth-boundary cache reset is asymmetric** (C2) — invalidate on login (merge), **remove** on logout (member-scoped data must vanish with no refetch window).
6. **All money is integer cents**, formatted only at the edge; **CSRF is double-submit, handled invisibly** in the client middleware on writes only.

---

## 8. The review + hardening pass

After the five chunks landed, Phase 2 went through a **6-dimension adversarial review** (authz/IDOR, Stripe-webhook, concurrency/money, data-model, best-practices/wiring, frontend — each finding adversarially verified). It found **0 critical / 0 high**. That's a stronger signal than "found and fixed a critical bug": it says the architecture held and the cleanup was hygiene.

### What held up (the good signal)

- **Stripe signature verification** via `EventUtility.ConstructEvent` (verify + parse atomic).
- **Order-creation idempotency** — the filtered-unique `UX_Payment_StripeSessionId` (migration `0005`) catching SQL `2601/2627` is a hard backstop the cheap read can't be raced past.
- **`RowVersion` optimistic concurrency** on `InventoryItem` and `Order` (the only two rowversioned tables) — proven by the last-unit race test.
- **Member RBAC scoping** — orders keyed by `CustomerProfileId`, IDOR-guarded in the query.
- **GST `long`-rounding reconciles** against Stripe's charge exactly (separate `"GST (10%)"` line + `AwayFromZero`).
- Refuted findings (good): refund double-restock and concurrent-refund are **false** (restock + status-flip share one tx guarded by `Order.RowVersion`); negative-`Reserved` and sweeper-tx-split are only reachable via the `§16`-deferred cluster.

### What got fixed (mapped to commits)

| Fix | What it was | Commit |
|---|---|---|
| **S1** | `[AllowAnonymous]` by-session lookup leaked **member PII** (member orders also carry the session id) → added `CustomerProfileId == null` | `7ab423d` |
| **C1** | `charge.refunded` full-restocked on **partial** refunds → `if (!charge.Refunded) return;` | `7ab423d` |
| **C2** | Cart cache **bled across the auth boundary** → `useSessionActions` (invalidate on login, remove on logout) | `7ab423d` |
| **B1** | `CartSweepService` left the last human as `UpdatedBy` → stamp `null` (principal-less actor) | `7ab423d` |
| **S3** | Stripe secrets not validated at boot → `AddOptions<StripeOptions>().ValidateOnStart()` gated to non-Dev | `90f4bee` |
| **C4** | Money **overflow** could be charged-then-rejected → `long` guard *before* reserving stock | `90f4bee` |
| **S2** | Stripe session id logged in plaintext via `{RequestPath}` → `LogPathSanitizer` redaction | `a3a32a4` |
| **C3** | Concurrent-cancel **double-refund** TOCTOU → transient `OrderStatus.Refunding=6` claimed atomically before Stripe | `f0485d9` |
| **B2/B3** | No DB single-open-cart guarantee + no reservation-owner CHECK → migration `0007` | `8510c7d` |

#### Migration `0007_constraint_hardening` (resume-gold) — pushing invariants into SQL

The two app-level invariants made physical. **Single open cart per identity:**

```csharp
migrationBuilder.CreateIndex(name: "UX_Cart_OpenPerAnonymousKey", table: "Cart",
    column: "AnonymousKey", unique: true, filter: "[Status] = 1 AND [AnonymousKey] IS NOT NULL");
migrationBuilder.CreateIndex(name: "UX_Cart_OpenPerProfile", table: "Cart",
    column: "CustomerProfileId", unique: true, filter: "[Status] = 1 AND [CustomerProfileId] IS NOT NULL");
```

A merge-on-login race or a double-create bug could otherwise leave two Open carts for the same person, **splitting their items silently**. The UNIQUE constraint makes that physically impossible. The filter does two jobs: `[Status] = 1` constrains only Open carts (history accumulates freely), and `IS NOT NULL` lets the same table hold member and guest carts without the `NULL` side tripping uniqueness (**SQL Server treats multiple `NULL`s as distinct only under a filtered index** — the predicate is mandatory).

**The reservation-owner XOR:**

```csharp
migrationBuilder.AddCheckConstraint(name: "CK_InventoryReservation_Owner", table: "InventoryReservation",
    sql: "([CartId] IS NOT NULL AND [OrderId] IS NULL) OR ([CartId] IS NULL AND [OrderId] IS NOT NULL)");
```

A reservation is a polymorphic hold owned by exactly one of a cart (pre-checkout) or an order (committed) — the commit re-homes it. Encoding the XOR as a CHECK makes "both set" or "neither set" unrepresentable, so a buggy commit path can't orphan or double-own a hold. It mirrors `CK_Order_Identity` from `0005` — **the same invariant asserted in C# *and* enforced by the DB.** The migration provides a real `Down()` (these can fail to apply on dirty data, so a clean rollback matters).

This pass is also where two earlier security fixes from the catalog/auth review carry forward in spirit: **PII never enters logs** (S2's `LogPathSanitizer` masks the session id out of `{RequestPath}`), and **secrets fail the boot loudly** (S3's `ValidateOnStart`). The deferred `§16` cluster (the `CheckingOut` state + reservation-expiry release + reconciliation job) is **honestly tracked, not silently dropped**.

### The review/hardening — what to know cold

1. **0 critical/high on a 6-dimension adversarial review** — the architecture held; fixes were hygiene, not firefighting.
2. **The headline security fix (S1)** — an `[AllowAnonymous]` endpoint leaked member PII because member orders *also* carry the session id; the one-line `CustomerProfileId == null` filter is the whole defense.
3. **The headline correctness fix (C3)** — concurrent cancel could double-refund; a transient `Refunding` state claimed atomically before the Stripe call caps the provider to once per order.
4. **Migration `0007` pushes two invariants into SQL** — filtered-unique single-open-cart indexes + the reservation-owner CHECK — defense-in-depth behind the app logic.
5. **The deferred `§16` cluster is tracked, not hidden** — it's genuinely Phase-8 event-driven-reconciliation work, and a loud-failure guard protects the interim.

---

## 9. File relationship maps

These trace the real Phase 2 flows end to end. Names, methods, routes, cookie names, and SQL are exactly as they appear in the code.

### Add to cart (guest) → the anon cookie

```
SPA add-to-cart → apiClient.POST('/api/v1/cart/items', { body: { productVariantId, quantity } })
   │  POST → csrfMiddleware attaches X-CSRF-Token (guests fetch a csrf token too)
   ▼
CartController.AddItem([FromBody] AddCartItemRequest)        [AllowAnonymous]
   │  validate (1..99) → 422 on bad shape
   │  caller = BuildCaller()  =  (_currentUser.UserId, Request.Cookies["anon_cart_key"])
   ▼
CartService.AddItemAsync(caller, request)
   │  ResolveAsync → no user id + no cookie? → CreateCartAsync(anonymousKey: newGuid)
   │  GetSellableVariantAsync (IsActive && Product.IsPublished; null ⇒ 404)
   │  line exists? bump Quantity = Min(q + req, 99)  :  add CartItem { UnitPriceCentsSnapshot = variant.PriceCents }
   │     └─ UX_CartItem_CartId_ProductVariantId forbids a duplicate line ⇒ bump is forced
   │  TouchExpiry → ExpiresAt = now + 30min
   │  SaveChangesAsync()
   ▼  CartResult(CartDto, AnonymousKey = "<guid>")
CartController: ApplyAnonymousCookie(key) → Set-Cookie anon_cart_key (HttpOnly, SameSite=Lax, Path=/)
   │   (Lax — so it survives the top-level navigation BACK from Stripe later)
   ▼
Ok(ApiResponse<CartDto>.Ok(cart))   →   SPA setQueryData(['cart'], cart) → page + header badge update
```

### Merge on login (lazy, first cart touch)

```
member (authed) revisits /cart, still holding a guest anon_cart_key cookie
   ▼
CartController.GetCart → caller = (userId, anonKey)
   ▼
CartService.GetCartAsync → ResolveAsync(create:false)
   │  profileId = _profiles.GetMyProfileAsync(userId).Id           ← lazy-creates the profile if absent
   │  memberCart = GetOpenCartByProfileAsync(profileId)
   │  guestCart  = GetOpenCartByAnonymousKeyAsync(anonKey)
   │  hasGuestItems? → create the member cart if needed
   │
   │  MergeInto(memberCart, guestCart):
   │     for each guest line: member line exists ? Quantity = Min(sum, 99) : add a copy
   │     guestCart.Status = Abandoned
   │  SaveChangesAsync()      ← merge + abandon committed ATOMICALLY (even on a GET)
   ▼  CartResult(merged cart, AnonymousKey = null)    ← members never keep an anon cookie
CartController: deletes the anon_cart_key cookie (matched Path/SameSite/Secure)
   │  (and on the frontend, useSessionActions already invalidated ['cart'] on login)
```

### Checkout start → reserve → Stripe redirect

```
CartPage "Proceed to checkout" → useStartCheckout → POST /orders/checkout-session { returnBaseUrl: origin }
   ▼
OrdersController.CreateCheckoutSession([FromBody] StartCheckoutRequest)   [AllowAnonymous]
   │  validate absolute http(s) → 422
   │  caller = (_currentUser.UserId, Cookies["anon_cart_key"])    ← cart from cookies, NEVER the body
   ▼
CheckoutService.StartCheckoutAsync(caller, request)
   │  cart = GetCartAsync(caller)        ← also the merge-on-login point
   │  subtotalLong = Σ (long)UnitPriceCents * Quantity ;  taxLong = round(subtotal * 0.10, AwayFromZero)
   │  if subtotalLong + taxLong > int.MaxValue → ConflictException (409)     ← C4 guard BEFORE reserve/charge
   │
   │  InventoryReservationService.ReserveCartAsync(cartId)
   │     └─ per line: read Available+RowVersion → fast-fail OutOfStock (409 INVENTORY_INSUFFICIENT)
   │        else Where(Id==id && RowVersion==read).ExecuteUpdate(Reserved += qty); 0 rows → 409 CONCURRENCY_CONFLICT
   │        whole cart in ONE transaction; 15-min hold
   │
   │  lineItems = [ per-variant CheckoutLineItem(name, UnitAmountCents, qty), CheckoutLineItem("GST (10%)", taxCents, 1) ]
   │  metadata = { cartId, customerProfileId? }     ← the bridge back to the domain for the webhook
   │  IStripeCheckoutGateway.CreateCheckoutSessionAsync(...)   ← Stripe SessionService.CreateAsync (lazy client)
   ▼  CheckoutSessionResponse(Url)
SPA: window.location.assign(url)   →   Stripe-hosted page (app never sees the card PAN)
```

### Webhook → order creation (idempotent, transactional)

```
Stripe POST /api/v1/payments/stripe/webhook   (server-to-server, no cookies)
   │  CsrfMiddleware: path == StripeWebhookPath → SKIP the double-submit check (signature is the auth)
   ▼
PaymentsController.StripeWebhook
   │  payload = raw body (verbatim bytes — model binding would break the HMAC)
   │  signature = Request.Headers["Stripe-Signature"]
   ▼
StripeWebhookService.HandleAsync(payload, signature)
   │  Event = EventUtility.ConstructEvent(payload, sig, WebhookSigningSecret, throwOnApiVersionMismatch:false)
   │      └─ verifies HMAC AND parses in one step (can't process an unverified payload)
   │  IsProcessedAsync(event.Id)? → log + return     ← fast-path skip (racy by design)
   │
   │  switch event.Type:
   │    "checkout.session.completed":
   │       read metadata cartId / customerProfileId ; address = CustomerDetails.Address (billing == shipping, MVP)
   │       → OrderCreationService.CreateOrderFromCheckoutAsync(CheckoutCompletion)
   │            GetByStripeSessionIdAsync? → return existing            ← idempotency layer 1 (cheap read)
   │            BeginTransaction:
   │               AddOrder(order graph) ; SaveChanges
   │                  └─ dup-key 2601/2627 on UX_Payment_StripeSessionId → rollback + return winner  ← layer 2 (race-proof)
   │               for each active hold: CommitReservedAsync (OnHand-=qty) ; reservation.OrderId=order.Id ; CartId=null
   │               cart.Status = Converted
   │               active.Count == 0 → ConflictException (fail LOUD, never undersell)
   │            commit
   │    "charge.refunded":
   │       !charge.Refunded (partial)? → log + return                  ← C1 skip
   │       → OrderRefundService.RefundByPaymentIntentAsync(...)
   │
   │  RecordAsync(event.Id)      ← AFTER side effects: a failure leaves it un-recorded for Stripe's retry
   ▼
200 OK  (StripeException → 400 stop-retrying ; processing failure → 500 retry-later)
```

### The last-unit race (two reservers)

```
cartA.ReserveCartAsync(variant, qty 1)        cartB.ReserveCartAsync(variant, qty 1)
  scope A / DbContext A / connection A           scope B / DbContext B / connection B
        │  read Available=1, RowVersion=R              │  read Available=1, RowVersion=R
        ▼                                              ▼
  UPDATE ... WHERE Id=i AND RowVersion=R         UPDATE ... WHERE Id=i AND RowVersion=R
  SQL Server re-stamps RowVersion = R'           (predicate still carries R, now stale)
        │  affected = 1  → WIN                         │  affected = 0  → ConcurrencyException → 409
        ▼                                              ▼   (or, if it read AFTER the win: Available 0 → OutOfStock → 409)
   Reserved = 1                                  no change
        └──────────────── final state: reserved == 1, exactly one winner ───────────────┘
```

### Customer cancel → claim → refund → reversal

```
OrderDetailPage "Cancel" (only when status == 'Paid') → POST /orders/{id}/cancel   [Authorize(Customer)]
   ▼
OrderCancellationService.CancelMyOrderAsync(orderId, appUserId)
   │  order = GetOwnedByIdAsync(orderId, profileId)   → null ⇒ 404 (IDOR: id AND owner in one WHERE)
   │  order.Status != Paid ⇒ 409 (fast, friendly — but NOT the concurrency boundary)
   │  paymentIntentId = GetChargePaymentIntentIdAsync(orderId)   (AmountCents > 0 = the charge)
   │
   │  TryClaimForRefundAsync: UPDATE ... WHERE Id=o AND owner AND Status=Paid SET Status=Refunding
   │     └─ affected == 1 ? WIN  :  0 ⇒ ConflictException 409     ← caps Stripe to ONE call per order (C3)
   │
   │  try   RefundAsync(pi)  with IdempotencyKey = "refund:{pi}"   ← Stripe collapses dup requests too
   │  catch ReleaseRefundClaimAsync (Refunding → Paid) ; rethrow   ← failed refund stays cancellable
   │
   │  OrderRefundService.RefundByPaymentIntentAsync(pi)   ← the SAME idempotent reversal the webhook uses
   │     already Refunded? return ; else BeginTransaction:
   │        for each line: RestockByVariantAsync (OnHand += qty)
   │        Status = Refunded ; Payments.Add(AmountCents = -TotalCents)     ← append-only ledger
   │        SaveChanges (Order.RowVersion serializes vs the charge.refunded webhook) ; commit
   ▼
   later: Stripe fires charge.refunded → OrderRefundService re-runs → already Refunded → no-op
```

### Checkout success (webhook-lag poll, C5)

```
Stripe redirects → /checkout/success?session_id=cs_test_...
   ▼
CheckoutSuccessPage → useOrderBySessionQuery(sessionId)
   │  GET /orders/by-session/{sessionId}   [AllowAnonymous]
   │     └─ OrderRepository: WHERE CustomerProfileId == null AND Payments.Any(StripeSessionId == id)   ← S1 guard
   │  retry WHILE status === 404 && count < 10, retryDelay 1500ms   ← webhook hasn't created the order YET
   ▼
   order lands  →  "Order #N confirmed"
   poll exhausted (isError) →  "Payment received / still finalising"  (soft — they WERE charged)
   ▼
   useEffect [order, isError]:  if (order || isError) invalidateQueries(['cart'])   ← clear ONLY on a terminal state
                                                                                      (never per poll tick — the C5 fix)
```

---

## 10. Patterns to remember

The Phase 2 additions to your interview toolkit, in rough priority order. (Phase 0's envelope/middleware/audit-interceptor and Phase 1's cookie-JWT/CSRF/vertical-slice/soft-delete patterns all still hold; these build on them.)

### 1. Idempotent webhook ingestion (highest priority)

**The pattern:** verify the signature over the **raw body** (`EventUtility.ConstructEvent`, which verifies *and* parses atomically); skip already-seen events via a fast-path ledger; do the side effects; **record the event AFTER success**. The hard idempotency guarantee is a **DB unique constraint** (`UX_Payment_StripeSessionId`) + `RowVersion`, not the ledger (which is racy by design).

**Why:** Stripe delivers **at-least-once**. Record-after-success converts that to **effectively-once** — a failure leaves the event un-recorded so the retry safely re-runs it. The status codes are a **retry contract**: 400 = stop (unverifiable), 500 = retry, 200 = done.

**Resume claim:** "Idempotent, signature-verified Stripe webhook tolerant of at-least-once delivery, with DB-unique-index dedup as the race-proof backstop."

### 2. Optimistic concurrency via `rowversion` + set-based `ExecuteUpdate`

**The pattern:** read the row's `RowVersion`; do `Where(Id == id && RowVersion == read).ExecuteUpdate(...)`; **check the affected-row count** — `0` means a concurrent writer re-stamped the rowversion and won, so you raise a 409.

**Interview gotcha:** `ExecuteUpdate` does **not** throw on a 0-row update — you inspect the count yourself. And it **bypasses the `AuditingInterceptor`**, so you stamp `UpdatedAt`/`UpdatedBy` by hand (the same lesson as Phase 1's set-based clears).

**Resume claim:** "Last-unit-oversell prevention via SQL Server `rowversion` optimistic concurrency, proven by a two-connection race test."

### 3. The snapshot pattern (freeze history at purchase time)

**The pattern:** an order stores **copies** — `UnitPriceCentsSnapshot`, `SkuSnapshot`, `NameSnapshot`, a JSON `OrderAddressSnapshot` — not FKs to live `ProductVariant`/`Address` rows.

**Why:** an order is **immutable history**. A later rename/reprice/delete must not rewrite what a past order cost or where it shipped — and a guest has no saved `Address` at all, so a snapshot is the only option. **Interview gotcha:** the JSON value object needs a `ValueComparer`, or EF misses in-place mutations.

### 4. `BackgroundService` + `PeriodicTimer` + a fresh scope per tick

**The pattern:** a hosted `BackgroundService` ticks a `PeriodicTimer` (built on an injected `TimeProvider`) and resolves its scoped, `DbContext`-backed worker from **`IServiceScopeFactory.CreateAsyncScope()` per tick**, inside a `try/catch`.

**Why:** a hosted service is a **singleton** — constructor-injecting a scoped service is a **captive dependency** (one `DbContext` shared forever). The `TimeProvider` makes the cadence fakeable; the per-tick `try/catch` keeps one bad sweep from killing the loop.

**Resume claim:** "Background sweeper reclaiming abandoned-cart stock, deterministically testable via an injected `TimeProvider`."

### 5. Money in integer cents + an overflow guard in `long`

**The pattern:** money is `int` cents everywhere (DB, API, Stripe, the React store). Before charging, compute the total in `long` and reject `> int.MaxValue` **before** reserving or calling Stripe. Tax is its own line item so the charged total reconciles to the stored total cent-for-cent.

**Interview gotcha:** ordering — reject the oversized cart *before* you take money, or you charge-then-fail (C4). And keep `GstRate`/rounding identical on both the checkout and order-creation sides.

### 6. Guest-vs-member identity: nullable XOR + a DB `CHECK`

**The pattern:** `Order.CustomerProfileId` and `Order.GuestEmail` are both nullable, with `CK_Order_Identity` enforcing **exactly one is set**. The app sets one; the DB guarantees the XOR.

**Why:** full guest checkout without fabricating accounts. **Interview gotcha:** nullable-on-both-sides alone permits both-null/both-set — the `CHECK` is what makes it an invariant.

### 7. Single-open-cart filtered unique index (TOCTOU defense)

**The pattern:** "at most one Open cart per owner" is a filtered unique index — `UNIQUE(CustomerProfileId) WHERE [Status] = 1 AND CustomerProfileId IS NOT NULL` (and the same for `AnonymousKey`). The app's fetch-or-create is the fast path; the index is the real guarantee.

**Interview gotcha:** the `IS NOT NULL` half is mandatory — SQL Server treats multiple `NULL`s as distinct only under a *filtered* unique index — and the `[Status] = 1` is the enum's magic-number value (renumbering the enum would break it).

### 8. CSRF-exempt webhook (the signature is the auth)

**The pattern:** the server-to-server webhook is `[AllowAnonymous]` and **path-exempt** from the CSRF middleware, because it carries no cookie and Stripe can't echo a double-submit token — its authentication is the `Stripe-Signature` HMAC.

**Why:** CSRF defends browser requests carrying ambient cookies; a webhook has neither. **Interview gotcha:** use `StartsWithSegments` (segment-aware), or `/webhook-evil` would slip the exemption.

### 9. Two-layer idempotency (cheap read + DB unique backstop)

**The pattern:** a cheap up-front read handles the common redelivery; a **DB unique constraint + catch** handles the genuine concurrent race (TOCTOU) the read can't. Order creation does both: `GetByStripeSessionIdAsync` then the `UX_Payment_StripeSessionId` dup-key catch → rollback → return the winner.

**Why:** the read alone has a window where two simultaneous webhooks both pass it; the unique index is what actually stops two orders from one session.

### 10. Server-state cache strategy: write-through / invalidate / remove

**The pattern:** mutations that return the authoritative resource **write it through** (`setQueryData`) — one round-trip, no refetch. At the **auth boundary**, the choice is deliberate: **invalidate** on login (data still yours, just merged/stale), **remove** on logout (member-scoped data must vanish with **no refetch window**).

**Resume claim:** "Eliminated cross-identity server-state bleed by centralizing TanStack Query cache resets at the auth boundary."

### 11. Webhook-lag polling on the success page

**The pattern:** the order is created by a webhook that **lags the redirect**, so the success page **polls** the by-session lookup (retry-while-404, bounded) and only declares success / clears the cart on a **terminal state** — never per poll tick.

**Why:** clearing optimistically on redirect would lose the cart if the webhook fails. The bounded poll gives the webhook a budget, then shows a reassuring soft fallback (the customer *was* charged).

### 12. Status-preserving client errors → branchable UI + smart retries

**The pattern:** a custom `ApiError` carries `response.status`, so the UI can render 401 vs 404 vs network distinctly, and retry policies **skip definitively-failed** requests (never retry a 404/401; retry only network/5xx, or — for the poll — only a 404).

**Interview gotcha:** on a true network failure `response` is `undefined`, which the UI maps to "check your connection," distinct from a real 404.

### 13. IDOR defense on reads: owner-in-the-query + 404-not-403 + the anonymous-bearer null filter

**The pattern:** owner-scoped reads filter `id AND ownerId` in **one query** (a miss is "doesn't exist *or* isn't yours," both → 404, never 403, so an id's existence is never confirmed). The anonymous by-session path is safe **only** because the repo adds `CustomerProfileId == null` (member orders also carry the session id).

**Why:** the session id is a high-entropy bearer, but member orders share it — the null-profile filter is the single clause standing between "guest confirmation page" and "member PII leak" (the S1 fix).

---

## 11. What's next

### Phase 3 — Admin Ops, Audit, 3-role RBAC (`PLAN.md:501`)

Phase 2 let a customer transact. Phase 3 is the **staff** side of the same orders:

| Story | What lands |
|---|---|
| **Admin shell + order workbench** | A hand-built `AdminShell`; staff view/search orders and **Mark Shipped** → the **`Shipment` entity** (deferred from Phase 2 — which is why `OrderStatus.Fulfilled = 3` exists in the enum but is **never set** in Phase 2). |
| **3-role RBAC** | The real `Administrator` / `StoreManager` / `Staff` policy matrix + UI guards — `StoreManager` finally gets backed admin routes (Phase 1/2 scoped `/admin` to `Administrator`-only precisely because no other role had endpoints). |
| **Audit viewer** | A read UI over the audit columns the `AuditingInterceptor` has been stamping since Phase 0. |
| **Reporting** | A sales-by-day report. |
| **E2E** | Vitest + Playwright golden-path tests (the project's first browser E2E in CI). |

Patterns Phase 3 will add: **policy-based authorization** beyond role checks, the **admin state-transition** workbench (the `Shipment` lifecycle), and **golden-path E2E** as a CI gate.

### Carried-forward follow-ups

- **The `§16` checkout-hardening cluster → Phase 8** — `docs/PHASE_2_SCOPE.md §16`: a `CheckingOut` cart state (rejects mutations, excluded from the sweeper, re-homed on merge) + reservation-expiry release + a **reconciliation job** that re-acquires or refunds when holds expired before a paid webhook. Genuinely event-driven-reconciliation territory — it belongs with the Service Bus / Event Grid work, and the loud-failure guard protects the interim.
- **Variant `DELETE` → deactivate**, now that `OrderLine` references variants (the `Restrict` FK already blocks a hard delete of an in-order variant).
- **Partial-refund support** — the admin refund UI (Phase 3) turns the currently-skipped partial `charge.refunded` into a real partial reversal.
- **Resume-gold rewrite-from-understanding queue** — `InventoryReservationService`, `OrderCreationService`, `StripeWebhookService` (the deepest files of the phase).

### Where to look up things later

- **"What did Phase 2 build?"** → this file
- **"What did Phase 1 / Phase 0 build?"** → `phase1_recap.md` / `phase0_recap.md` (same folder)
- **"Why did Phase 2 decide X?"** → `docs/PHASE_2_SCOPE.md` (authoritative for the phase; outranks the older docs where they disagree)
- **"What does this specific file do?"** → the heavy comment block at the top of every file
- **"What's the current task / where are we?"** → memory's `project_progress.md`
- **"What's the data model / locked stack?"** → `docs/DATABASE_DESIGN.md`, `docs/PLAN.md`, memory's `tech_decisions.md`
