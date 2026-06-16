# Phase 2 — Cart & Orders: Implementation Scope

> **Status:** scoped 2026-06-16, not yet started. Companion to `PLAN.md` (§13 Phase 2,
> line 492), `REQUIREMENTS.md` (Epic 2), and `DATABASE_DESIGN.md` (§3.8–§3.22). Where
> this doc and those disagree, **this doc wins for Phase 2** — the deltas are called out
> in §4 and §3 and are deliberate.

---

## 1. Goal & demo target

Turn the read-only catalogue (Phase 1) into a transacting store: a shopper — **logged in
or not** — adds variants to a cart, checks out through Stripe's hosted page with a test
card, and an order is created off the back of an idempotent webhook while stock is
decremented under optimistic concurrency.

**Demo:** add to cart → Stripe test card `4242…` → webhook fires → order appears (via the
account "My Orders" page for members, or a tokenised success link for guests); inventory
drops; a duplicate webhook is ignored; two shoppers racing for the last unit → exactly one
wins (the other gets `409 INVENTORY_INSUFFICIENT`).

---

## 2. Scope boundary

**Phase 2 owns**

- Entities + EF config + migration `0004_orders` (8 tables, see §5) including the
  `Seq_OrderNumber` sequence.
- Cart endpoints + storefront cart UI; **anonymous cart + merge-on-login**.
- Stripe **hosted** Checkout session creation; webhook **landing on the API** (see §3.1).
- Inventory two-phase reserve → commit with `InventoryItem.RowVersion`; `CartExpirySweeper`.
- Customer order viewing (list / detail / cancel) **+ guest order lookup**.
- xUnit integration tests: full checkout, duplicate-webhook idempotency, concurrent-last-unit.

**Phase 2 does NOT own (deferred)**

- Azure Functions + Event Grid + Service Bus fan-out — **Phase 8** (§3.1).
- `Shipment` table + "Mark Shipped" + staff order workbench — **Phase 3**.
- Vouchers / loyalty / the full pricing pipeline — **Phase 7** (`OrderPriceBreakdown` is
  created now but only its subtotal/tax/shipping/total fields are populated).
- Refund UI for staff/admin — **Phase 3**; Phase 2 only does customer self-cancel refund.

---

## 3. Key decisions (2026-06-16)

### 3.1 Stripe webhook lands on the API now, not an Azure Function

`PLAN.md` says the webhook should route `APIM → Event Grid → StripeWebhookHandlerFn`
"from day 1". **We are not doing that in Phase 2.** No Azure Functions project exists; the
`functions`/`eventGrid`/`serviceBus` Bicep modules are placeholder stubs deploying zero
resources; Event Grid has no local emulator, so a Function webhook could not be exercised
through the existing `ApiFactory` integration harness.

**Decision:** implement the webhook as an MVC controller action under
`src/api/Retail.Api/Payments/`, at the **route-stable** path
`POST /api/v1/payments/stripe/webhook`. Phase 8 moves the *handler body* behind a Function
+ Event Grid without changing that public route, so nothing downstream breaks.

### 3.2 Orders with AND without login (full guest checkout)

The shopper explicitly wants to **place orders without an account**. This supersedes
`DATABASE_DESIGN.md §3.11`, which makes `Order.CustomerProfileId` NOT NULL.

- `Order.CustomerProfileId` becomes **nullable**; add `Order.GuestEmail` (nvarchar 256).
- **Invariant (app-enforced):** an order has exactly one identity source —
  `CustomerProfileId` XOR `GuestEmail`.
- **Cart identity:** logged-in → cart owned by `CustomerProfileId`; guest → cart keyed by
  an `X-Anon-Cart-Key` cookie. On login, the anon cart is **merged** into the account cart.
- **RBAC change:** cart, checkout-session, webhook, and guest-order-lookup endpoints are
  `[AllowAnonymous]` (this relaxes the "Customer-only" matrix in `REQUIREMENTS.md`, which
  is reconciled here). Account order list/cancel stay `[Authorize(Roles.Customer)]`.
- **Guest order retrieval (IDOR-safe):** at order creation we mint an **opaque
  HMAC-signed order-access token** (reusing the `Csrf:Key` signing pattern). It rides the
  Stripe success redirect (`/checkout/success?orderId=…&t=<token>`), and a guest reads the
  order via `GET /api/v1/orders/{id}?t=<token>` `[AllowAnonymous]` — the signature stops id
  enumeration. Guests get **no** account-scoped order list. Members ignore the token and
  use the account-scoped path.

### 3.3 Entity cut-line

- **`OrderPriceBreakdown` is created in Phase 2** (PLAN line 493), populated with
  subtotal/tax/shipping/total only; voucher/loyalty fields stay 0 until Phase 7.
- **`Shipment` is deferred to Phase 3** (where "Mark Shipped" lives), despite
  `REQUIREMENTS`/`DATABASE_DESIGN` listing it in the orders migration.

### 3.4 Cart UI: page first, drawer later

No `Drawer`/`Sheet`/`Dialog` primitive exists, and the codebase favours full pages over
modals (it uses `window.confirm`). Phase 2 ships a `/cart` **page** + a header badge count.
A drawer primitive is an optional later polish.

---

## 4. Doc-vs-code drifts this phase fixes (recon-verified)

| # | Drift | Reality / fix |
|---|---|---|
| 1 | DB-design/REQUIREMENTS call the migration `0002_orders` | `0002` is catalog, `0003` is profile → **`0004_orders`** |
| 2 | DB-design says `Seq_OrderNumber` shipped with catalog | It was **never created** (0 `HasSequence` hits) → Phase 2 creates it |
| 3 | DB-design/CODING_STANDARDS reference a `ConcurrencyException` | It **does not exist**; `DbUpdateConcurrencyException`→409 is already mapped. We add a thin domain `ConcurrencyException` for clarity + an `OutOfStockException` for the pre-check path |
| 4 | DB-design §1 says audit columns are `datetime2(7)` | Phase 1 uses `DateTimeOffset` everywhere → Phase 2 matches Phase 1 (DateTimeOffset) |
| 5 | Docs imply CI splits unit vs integration (`--filter Category=`) | CI runs a single `dotnet test`; the split is aspirational. We keep a single run |
| 6 | One sweep interval is implied | **Three distinct concepts:** reservation TTL = 15 min, cart TTL = 30 min, sweeper cadence = 5 min |
| 7 | `newsequentialid()` claimed for GUID PKs | EF generates GUIDs client-side today; Phase 2 **keeps** that (consistency with Phase 1) |

These doc files are **not** rewritten as part of Phase 2 code; this table is the
reconciliation of record. A later docs pass can fold them back in.

---

## 5. Data model — migration `0004_orders`

All money is `int` cents. All status columns are **byte-backed C# enums → `tinyint`,
1-based, default 1** (the project's first persisted enums). All entities implement
`IAuditableEntity` **except** `ProcessedStripeEvent` (technical append table). None are
soft-deletable (Cart/Order use a status instead). FK/index/Delete behaviour mirrors Phase 1
conventions (named `UX_`/`IX_` indexes, explicit `OnDelete`).

### 5.1 Status enums (`Common/Enums/`)

| Enum | Values |
|---|---|
| `CartStatus` | `Open=1, Abandoned=2, Converted=3` |
| `ReservationStatus` | `Active=1, Committed=2, Released=3` |
| `OrderStatus` | `Pending=1, Paid=2, Fulfilled=3, Cancelled=4, Refunded=5` |
| `PaymentStatus` | `Created=1, Succeeded=2, Failed=3, Refunded=4` |

### 5.2 Entities

- **Cart** — `Id` (Guid), `CustomerProfileId` (Guid?, null for guest), `AnonymousKey`
  (char(36)?, null for member), `Status` (CartStatus, def 1), `ExpiresAt` (DateTimeOffset,
  30-min sliding). Indexes: `IX_Cart_CustomerProfileId_Status`,
  `IX_Cart_AnonymousKey_Status` (filtered `AnonymousKey IS NOT NULL`),
  `IX_Cart_ExpiresAt_Status`. No soft-delete, no RowVersion.
- **CartItem** — `Id`, `CartId` (FK Cascade), `ProductVariantId` (FK Restrict),
  `Quantity` (>0), `UnitPriceCentsSnapshot`. `UX_CartItem_CartId_ProductVariantId`
  (unique, dedup) + `IX_CartItem_CartId`.
- **InventoryReservation** — `Id`, `InventoryItemId` (FK), `CartId` (Guid?) + `OrderId`
  (Guid?) — exactly one set, `Quantity` (>0), `ExpiresAt` (15-min TTL), `Status`
  (ReservationStatus, def 1). Indexes: `IX_..._InventoryItemId_Status`,
  `IX_..._ExpiresAt_Status` (sweeper), `IX_..._CartId`, `IX_..._OrderId`.
- **Order** — `Id`, `OrderNumber` (int, `DEFAULT NEXT VALUE FOR Seq_OrderNumber`),
  `CustomerProfileId` (Guid?, **nullable** — guest), `GuestEmail` (nvarchar 256, nullable),
  `Status` (OrderStatus, def 1), `SubtotalCents`, `TaxCents` (def 0), `ShippingCents`
  (def 0), `TotalCents`, `ShippingAddressJson` + `BillingAddressJson` (nvarchar(max)
  snapshots via the ValueConverter+ValueComparer pattern), `PlacedAt`, **`RowVersion`**
  (`byte[]`, `.IsRowVersion()` — one of only two rowversioned tables). Indexes:
  `UX_Order_OrderNumber`, `IX_Order_CustomerProfileId_PlacedAt`, `IX_Order_Status_PlacedAt`.
- **OrderLine** — `Id`, `OrderId` (FK Cascade), `ProductVariantId` (FK Restrict),
  `Quantity`, `UnitPriceCents`, `LineTotalCents` (stored), `SkuSnapshot` (64),
  `NameSnapshot` (200). Indexes: `IX_OrderLine_OrderId`, `IX_OrderLine_ProductVariantId`.
- **Payment** — `Id`, `OrderId` (FK), `Provider` (def 'stripe'), `StripeSessionId`
  (120?), `StripePaymentIntentId` (120?), `AmountCents` (negative = refund), `Currency`
  (char(3), `IsFixedLength`, def 'AUD'), `Status` (PaymentStatus, def 1), `RawPayloadJson`
  (nvarchar(max)?). Indexes: `IX_Payment_OrderId` + filtered-NOT-NULL on the two Stripe ids.
- **ProcessedStripeEvent** — **bigint identity PK** (NOT Guid), `StripeEventId`
  (nvarchar 80, `UX_ProcessedStripeEvent_StripeEventId` — the idempotency key), `EventType`
  (80), `ReceivedAt` (datetime2 def `sysutcdatetime()`). **No `IAuditableEntity` block.**
- **OrderPriceBreakdown** — 1:1 with Order, `Id`, `OrderId` (FK, `UX_..._OrderId`),
  `SubtotalCents`, `VoucherDiscountCents` (def 0), `LoyaltyRedeemDiscountCents` (def 0),
  `ShippingCents` (def 0), `TaxCents` (def 0), `TotalCents`, `PipelineVersion` (def 'v1').
  Voucher/loyalty stay 0 in Phase 2.

### 5.3 Sequence

`builder.HasSequence<int>("Seq_OrderNumber").StartsAt(10001).IncrementsBy(1);` in
`OnModelCreating`, then `Order.OrderNumber` → `HasDefaultValueSql("NEXT VALUE FOR Seq_OrderNumber")`.
PK stays a GUID; `OrderNumber` is the human-facing reference.

---

## 6. Concurrency & transaction design

- **Reserve (add-to-cart-checkout):** within a transaction, for each line decrement is
  guarded — `Where(i => i.Id == id && i.RowVersion == original).ExecuteUpdateAsync(... Reserved += qty)`.
  Pre-check `Available < qty` → throw `OutOfStockException` → **409 `INVENTORY_INSUFFICIENT`**.
  Affected-rows == 0 (a racer won) → `DbUpdateConcurrencyException`/`ConcurrencyException` →
  **409 `CONCURRENCY_CONFLICT`**.
- **Commit (on `checkout.session.completed`):** transaction — flip reservations to
  `Committed`, `OnHand -= qty` / `Reserved -= qty`, create `Order`+`OrderLine`+
  `OrderPriceBreakdown`, mark cart `Converted`.
- **Release (sweeper / cart edit / abandon):** `Reserved -= qty`, reservation `Released`.
- Set-based `ExecuteUpdateAsync` bypasses `AuditingInterceptor`, so `UpdatedAt/UpdatedBy`
  are stamped manually from `TimeProvider` + `ICurrentUserAccessor` (the Phase 1 pattern).

---

## 7. Stripe integration

- **Package:** `Stripe.net` in `Retail.Api.csproj`. `StripeOptions` (SecretKey,
  WebhookSigningSecret) bound in `Program.cs` like `JwtOptions`/`CsrfOptions`.
- **Checkout:** `POST /api/v1/orders/checkout-session` reserves stock, creates a hosted
  Checkout Session (success/cancel URLs from `window.location.origin`), stashes cart-owner
  identity in **session metadata**, returns the session URL; frontend `window.location.assign`s.
- **Webhook:** `POST /api/v1/payments/stripe/webhook` `[AllowAnonymous]`, **CSRF-exempt**
  (see §11), reads the **raw body**, verifies via `EventUtility.ConstructEvent`
  (signing secret), dedupes on `ProcessedStripeEvent.StripeEventId`. Handles
  `checkout.session.completed` (commit + create order) and `charge.refunded`
  (Order→Refunded + inventory rollback). Loyalty claw-back is Phase 7.

---

## 8. API surface

| Method | Route | Auth | Notes |
|---|---|---|---|
| GET | `/api/v1/cart` | AllowAnonymous | resolves member vs anon-key cart |
| POST | `/api/v1/cart/items` | AllowAnonymous | `{variantId, quantity}` → 200/409 |
| PUT | `/api/v1/cart/items/{variantId:guid}` | AllowAnonymous | `{quantity}` |
| DELETE | `/api/v1/cart/items/{variantId:guid}` | AllowAnonymous | remove line |
| DELETE | `/api/v1/cart` | AllowAnonymous | clear |
| POST | `/api/v1/orders/checkout-session` | AllowAnonymous | guest must supply email+address |
| POST | `/api/v1/payments/stripe/webhook` | AllowAnonymous + CSRF-exempt | Stripe-signature auth |
| GET | `/api/v1/orders` | Customer | account-scoped, paged |
| GET | `/api/v1/orders/{id:guid}` | Customer **or** `?t=<token>` | member-scoped or guest-token |
| POST | `/api/v1/orders/{id:guid}/cancel` | Customer | Pending → refund + rollback |

Conventions mirrored: `ApiResponse<T>` envelope, 422 validation via the explicit
`ValidateAsync` helper, `PagedResult<T>`, PascalCase query params, trailing
`CancellationToken`, `[ProducesResponseType]` per status.

---

## 9. Frontend surface

- `features/cart/` — `CartPage`, `CartLineItem`, `CartSummary`, `useCartQuery` +
  `useCartMutations`, `cartKeys` factory; add-to-cart on `ProductDetailPage`; header badge.
- `features/checkout/` — "Proceed to checkout" mutation → `window.location.assign(url)`;
  `/checkout/success` return page (token-aware).
- `features/orders/` — `OrdersPage` (list) + `OrderDetailPage`; `orderKeys` factory; status
  `Badge` helper; routes under `RoleGuard allowedRoles={['Customer']}` (success page open).
- Regenerate `schema.d.ts` (`gen:api`) after backend endpoints exist; add `types.ts` aliases.

---

## 10. Exceptions & error codes

- New: `OutOfStockException` → **409 `INVENTORY_INSUFFICIENT`**, `ConcurrencyException` →
  **409 `CONCURRENCY_CONFLICT`** (or reuse the existing `DbUpdateConcurrencyException` arm).
  Optional `PaymentDeclinedException` → **402 `PAYMENT_DECLINED`** (confirm the frontend
  interceptor handles 402; otherwise model in-controller like `AuthController.MapError`).
- Each new exception = a `sealed : Exception` with a single `(string message)` ctor **plus**
  its own arm in `ExceptionMiddleware` (the code lives in the switch, not the exception).
- **CSRF exemption:** `CsrfMiddleware` is method-only today (no per-route opt-out). Add a
  path check in `InvokeAsync` to skip enforcement for the webhook route.

---

## 11. Testing plan

- **Integration (`ApiFactory`, real SQL + Azurite, `[Collection("api")]`, unique data/test):**
  full checkout flow; **duplicate-webhook idempotency**; **concurrent-buy-last-unit**
  (one 200, one 409 — rowversion needs real SQL Server, not SQLite).
- **Webhook tests are hermetic:** inject `Stripe:WebhookSigningSecret` into `ApiFactory`'s
  in-memory config; build the raw event body in-test and compute the `Stripe-Signature`
  header (HMAC) — `EventUtility.ConstructEvent` verifies offline, **no network/CLI**.
- **Unit:** plain `Assert.*` + hand-rolled fakes (`FixedClock : TimeProvider`, stub
  interfaces) — **no Moq/FluentAssertions** (the CODING_STANDARDS example is aspirational).
  Cover cart-merge, reservation lifecycle, price computation.
- **Load:** add `tests/load/checkout-flow.js` + a `docs/perf/baseline-{date}.md` entry.

---

## 12. Env / secrets (new)

`Stripe:SecretKey` (`sk_test_…`) and `Stripe:WebhookSigningSecret` (`whsec_…`):
1. blank placeholder + `_comment` in `appsettings.Development.json`;
2. `dotnet user-secrets set Stripe:SecretKey …` documented in README;
3. fixed test values injected in `ApiFactory.ConfigureWebHost`;
4. Key Vault in prod. Stripe CLI (`stripe listen --forward-to …`) only for *live* local
   webhooks — tests don't need it.

---

## 13. Chunking (each independently buildable + verifiable)

| Chunk | Deliverable | Verification |
|---|---|---|
| **0 — Data model** | 4 enums, 8 entities, 8 configs, DbSets, `Seq_OrderNumber`, migration `0004_orders` | `dotnet ef database update` applies; snapshot diff reviewed; build green |
| **1 — Cart** | repo+service+controller, anon-key + merge-on-login, DTOs/validators/mappers, frontend cart + add-to-cart + badge | add/update/remove/clear; cart survives login (merge); integration tests; gates green |
| **2 — Inventory + concurrency** | `InventoryReservation` lifecycle, RowVersion decrement, `OutOfStock`/`Concurrency` exceptions, `CartExpirySweeper` | concurrent-buy-last-unit test (one 200 / one 409); sweeper releases stale reservations |
| **3 — Stripe checkout + webhook** | Stripe.net + options + secrets, checkout-session endpoint, webhook (CSRF-exempt, sig-verify, idempotent), order creation, refund handler, frontend checkout + success page | hermetic checkout + duplicate-webhook tests; live `stripe listen` smoke |
| **4 — My Orders** | account list/detail/cancel, guest token lookup, frontend orders pages | cancel-Pending refunds + rolls back stock; guest token view works, id-enumeration blocked |

---

## 14. Resume-bullet alignment

- **A-1** (transactional platform, 500+ txn p95): cart→checkout→order is the core that
  claim measures; `tests/load/checkout-flow.js` is added now (the p95 number comes from the
  Phase 6/10 deployed env).
- **A-2 / B-3** (15+ endpoints, pagination/filter over the *order* entity): the order
  endpoints + `IX_Order_*` indexes are the named "order entity".
- **A-3** (idempotent Stripe webhooks, event-driven): `ProcessedStripeEvent` dedupe + the
  duplicate-webhook test are direct evidence; 10K/day + 70%-sync-reduction numbers are Phase 8.
- **A-4 / B-4** (xUnit, 85% coverage, 100+ tests): the three mandated integration tests +
  unit tests push the count toward 100+.

---

## 15. Open items / follow-ups

- Confirm `PaymentDeclinedException` → 402 vs in-controller mapping (frontend 402 handling).
- Auto-create a real account from a guest email at checkout? (Would give guests a future
  "My Orders"; currently out of scope — guests use the token link only.)
- A later docs pass to fold §4 reconciliations back into `DATABASE_DESIGN.md`/`REQUIREMENTS.md`.
- Optional `Drawer`/`Sheet` primitive for a cart drawer (deferred; page-first for now).
