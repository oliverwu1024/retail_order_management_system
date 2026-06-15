# Phase 1 Recap — What You Built and Why

> A self-learning recap of every concept, file, and connection introduced in
> Phase 1 (Stories 1.1–1.4 + the perf baseline + the security hardening pass).
> Read top to bottom the first time; later, use the table of contents to jump
> back to specific patterns. Companion to `phase0_recap.md` — Phase 0 wired the
> seams, Phase 1 filled them with real features.

## Table of contents

1. [The big picture — what Phase 1 turned on](#1-the-big-picture)
2. [Story 1.1 — Auth (cookie JWT + refresh rotation + CSRF)](#2-story-11--auth-cookie-jwt--refresh-rotation--csrf)
3. [Story 1.2 — Catalog (products, variants, inventory, image upload)](#3-story-12--catalog-products-variants-inventory-image-upload)
4. [Story 1.3 — Frontend (storefront + admin + auth UI)](#4-story-13--frontend-storefront--admin--auth-ui)
5. [Story 1.4 — Customer profile + addresses](#5-story-14--customer-profile--addresses)
6. [Perf baseline + the security hardening pass](#6-perf-baseline--the-security-hardening-pass)
7. [File relationship maps](#7-file-relationship-maps)
8. [Patterns to remember (interview material)](#8-patterns-to-remember)
9. [What's next — Phase 2 preview](#9-whats-next)

---

## 1. The big picture

### What Phase 1 turned on

Phase 0 left you with a **bootable, observable skeleton** — every cross-cutting seam wired but dormant: an empty `IAuditableEntity`, a JWT pipeline with no login, an `ApiResponse<T>` envelope with nothing to wrap, a `RoleGuard` with no roles. Phase 1 is where the lights come on. You shipped four end-to-end stories plus a perf baseline and a security pass:

| Story | What shipped |
|---|---|
| **1.1 Auth** | Register / login / refresh / logout / me — access JWT in an **HttpOnly cookie**, opaque refresh token with **single-use rotation + reuse detection**, **signed double-submit CSRF**, 4 seeded roles + a default admin. |
| **1.2 Catalog** | Products + variants + inventory + categories; anonymous storefront reads, Administrator writes; soft-delete; **image upload to Blob/Azurite** (magic-byte validated). |
| **1.3 Frontend** | The React storefront (browse grid + detail), the admin product-management UI (list + create/edit + image + variants), the auth UI (login + register + session bootstrap). |
| **1.4 Profile** | Customer profile + a multi-address book with a DB-enforced "one default per axis" invariant. |
| **Perf + hardening** | A k6 catalog load baseline, and an adversarial security/best-practices review (**0 high, 0 medium**) whose whole set of low/info findings you fixed. |

The "Phase 0 wired it → Phase 1 activated it" mapping made concrete:

| Phase 0 seam | Phase 1 activation |
|---|---|
| `IAuditableEntity` (no implementers) | Every domain entity implements it → the `AuditingInterceptor` now stamps on every save |
| `ApiResponse<T>` envelope | Every real endpoint (auth, catalog, profile) returns it; `PagedResult<T>` rides inside it for lists |
| JWT Bearer pipeline | Login mints tokens into cookies; `OnMessageReceived` reads the access token from the cookie; `[Authorize]` is real |
| CSRF middleware (frontend interceptor only) | The backend `CsrfMiddleware` lands; the signed double-submit handshake is live end to end |
| `RoleGuard` (no roles) | Routes guard on real role claims; the backend `[Authorize(Roles = …)]` is the actual gate |
| `RetailDbContext` (Identity tables only) | Migrations `0001`–`0003` add refresh tokens, the catalog, and the profile/address tables |
| `openapi-fetch` typed client | Real calls: `apiClient.POST('/api/v1/auth/login', …)`, generated types from `pnpm gen:api` |

### The vertical slice — the shape every feature took

Almost every Phase 1 feature is the same five-layer slice. Learn it once and you've read the whole codebase:

```
React feature (TanStack Query hook / RHF+zod form)
   │  apiClient.GET/POST(...)  — typed, CSRF header auto-attached
   ▼
Controller         ← binds params, runs the FluentValidation validator, wraps in ApiResponse<T>.
   │                  NO business logic. [Authorize] / [AllowAnonymous] is the real gate.
   ▼
Service            ← the business rules. Throws NotFoundException / ConflictException.
   │                  Owns multi-table transactions. The only layer that "decides" things.
   ▼
Repository         ← pure persistence. AsNoTracking on reads, Include for navs, ExecuteUpdate for set-based writes.
   │
   ▼
RetailDbContext    ← EF Core. Soft-delete global query filters + the AuditingInterceptor stamp on SaveChanges.
   │
   ▼
SQL Server         ← money as int cents, filtered unique indexes, rowversion where concurrency matters.
```

Every response — a product page, a 404, a validation failure, a CSRF rejection — comes back in the **same `ApiResponse<T>` envelope** with a `traceId`. That contract, established in Phase 0, is what lets the SPA have exactly one response handler.

### Why these choices matter for the resume

| Resume claim | The Phase 1 evidence |
|---|---|
| "ASP.NET Identity + JWT in HTTP-only cookies, refresh rotation, CSRF" | `Identity/`, `Services/AuthService.cs`, `Middlewares/CsrfMiddleware.cs`, ADR-0007 |
| "Three-tier vertical slices: Controller → Service → Repository" | Catalog + profile slices, end to end |
| "Soft-delete, filtered unique indexes, optimistic concurrency, audit interceptors" | `RetailDbContext` query filters, `AddressConfiguration` filtered indexes, `AuditingInterceptor` |
| "Secure file upload to Azure Blob (content sniffing, size limits)" | `Storage/BlobStorageClient.cs`, `Common/Helpers/ProductImage.cs` |
| "React 19 + TanStack Query + React Hook Form + zod, typed OpenAPI client" | `src/web/src/features/*`, `lib/api/client.ts` + generated `schema.d.ts` |
| "k6 load baseline + SLO thresholds" | `tests/load/catalog-browse.js`, `docs/perf/baseline-2026-06-15.md` |
| "Security review: 0 high/medium; hardened headers, CSRF key separation, supply-chain SHA-pinning" | The `harden(*)` commits; `Program.cs` security headers; `.github/` SHA-pinned actions + Dependabot |

---

## 2. Story 1.1 — Auth (cookie JWT + refresh rotation + CSRF)

This is the slice where Phase 0's wired-but-dormant auth seams come alive. The headline design is **ADR-0007**: the access JWT lives in an **HttpOnly cookie** (not `localStorage`, not the response body), the refresh token is an **opaque random string stored only as a SHA-256 hash**, rotated single-use with **reuse detection**, and writes are guarded by a **signed double-submit CSRF** token. Every choice below traces back to "what happens when an attacker has XSS, or a stolen cookie, or a malicious cross-site form?"

### What is in the auth slice

```
Retail.Api/
├─ Identity/
│  ├─ JwtOptions.cs                ← typed view of the Jwt config section (Issuer/Audience/Key/lifetimes)
│  ├─ CsrfOptions.cs               ← typed view of the Csrf section — a DEDICATED HMAC key
│  ├─ AuthSettings.cs              ← Auth:SecureCookies switch (HTTPS-only cookies, default true)
│  ├─ DefaultAdminOptions.cs       ← seeded admin email/password (secret, never committed)
│  ├─ IJwtService.cs / JwtService.cs       ← mints signed HS256 access tokens (sub→NameIdentifier, role claims)
│  ├─ ICsrfTokenService.cs / CsrfTokenService.cs  ← issues/validates {random}.{HMAC} CSRF tokens (stateless)
│  ├─ AuthCookies.cs               ← the ONE place all cookie security flags are set
│  ├─ AuthResult.cs               ← typed result (AuthError enum + AuthTokens record) — no exceptions for control flow
│  └─ IdentityDataSeeder.cs        ← idempotent startup seed of 4 roles + default admin
├─ Services/
│  ├─ IAuthService.cs              ← HTTP-agnostic auth contract (register/login/refresh/logout/me)
│  └─ AuthService.cs               ← orchestrates Identity + JWT + refresh rotation + reuse detection (resume-gold)
├─ Repositories/
│  ├─ IRefreshTokenRepository.cs   ← pure persistence contract for RefreshToken rows
│  └─ RefreshTokenRepository.cs    ← EF Core impl (get-by-hash, list-not-revoked-by-user)
├─ Middlewares/CsrfMiddleware.cs   ← 403s any state-changing request lacking a valid signed double-submit token
├─ Controllers/AuthController.cs   ← /api/v1/auth/{csrf,register,login,refresh,logout,me} — turns AuthResult into Set-Cookie
├─ Common/
│  ├─ Constants/Roles.cs           ← canonical 4 role names (single source of truth)
│  ├─ Constants/AuthConstants.cs   ← wire names for the 3 cookies + the CSRF header
│  └─ Helpers/SecureTokens.cs      ← CSPRNG token, SHA-256-at-rest, constant-time compare
├─ Validators/
│  ├─ RegisterRequestValidator.cs  ← email + display name + ≥12-char password (letter + digit)
│  └─ LoginRequestValidator.cs     ← shape-only check (don't touch the DB for malformed input)
├─ Domain/Entities/RefreshToken.cs ← the persisted hash + lifecycle columns (RevokedAt/ReplacedBy/Reason)
└─ DTOs/Responses/AuthUserDto.cs   ← the ENTIRE success body (id/email/name/roles) — carries no secret
```

### Per-file purpose

#### `Identity/JwtService.cs`

Mints the signed access token and nothing else — it knows nothing about cookies, refresh, or HTTP. Two claim choices are load-bearing for the rest of the stack:

```csharp
new(JwtRegisteredClaimNames.Sub, user.Id),      // sub → ClaimTypes.NameIdentifier on the validated principal
// ...
claims.Add(new Claim(ClaimTypes.Role, role));   // makes [Authorize(Roles = ...)] work with zero extra config
```

The `sub` claim round-trips: JwtBearer's default inbound mapping turns `sub` into `ClaimTypes.NameIdentifier`, which is exactly what the **AuditingInterceptor** (`CreatedBy`/`UpdatedBy`) and `AuthController.Me` read back via `User.FindFirstValue(ClaimTypes.NameIdentifier)`. A `jti` (GUID) is added so two tokens minted in the same second still differ. It's a **singleton** — immutable after construction, and `JwtSecurityTokenHandler` creation is thread-safe — with the clock injected via `TimeProvider` so tests mint at a fixed instant.

#### `Identity/CsrfTokenService.cs` + `Common/Helpers/SecureTokens.cs`

The CSRF token is **self-contained**: `{random}.{base64url(HMACSHA256(serverKey, random))}`. Validation re-signs `parts[0]` and constant-time-compares it to `parts[1]` — **no server-side state**, just the key. **Interview gotcha:** why HMAC-sign a double-submit token at all? Naive double-submit (cookie value == header value) breaks the moment an attacker can *plant* a cookie on your domain (sibling-subdomain XSS, an HTTP MITM): they set a known value and echo it. The signature closes that — an attacker can't forge a value `Validate()` accepts without the key. This is OWASP's **signed double-submit**, chosen over both naive double-submit and the framework's `IAntiforgery`.

`SecureTokens` is the reviewed home for three easy-to-botch crypto operations: `NewToken` (32 bytes from `RandomNumberGenerator`, base64url), `Sha256` (hex), and `FixedTimeEquals` (constant-time). **Why SHA-256 and not PBKDF2 for the refresh token at rest?** Because the input is *already* 256 bits of CSPRNG entropy — there is nothing to brute-force, so a slow password hash buys nothing.

#### `Identity/AuthCookies.cs` (resume-gold)

The single auditable place every cookie flag lives. Three cookies, three different postures:

- **`access_token`** — `HttpOnly` (XSS can't read it), path `/`, expires at the JWT's expiry.
- **`refresh_token`** — `HttpOnly`, longer-lived, and **scoped to `Path = /api/v1/auth`** so it's only sent to refresh/logout — least privilege, not attached to every API call. **Interview gotcha:** `Clear()` must delete it with the *same* path, or the browser treats it as a different cookie and never removes it.
- **`csrf`** — **non-HttpOnly on purpose**, so the SPA can read it and echo it in the `X-CSRF-Token` header; issued as a session cookie.

All carry `SameSite=Strict`, `Secure` (driven by `AuthSettings.SecureCookies`), and `IsEssential = true` so the GDPR cookie-consent gate doesn't suppress them.

#### `Services/AuthService.cs` (resume-gold)

The heart of the slice, and deliberately **HTTP-agnostic** — it returns an `AuthResult` carrying *raw* tokens and never touches `HttpContext`, so every path is unit-testable without a web host. Four things to know cold:

1. **The login timing-equalizer.** A static `DummyPasswordHash` is precomputed once. On the unknown-email branch, login still burns an equivalent PBKDF2 verify before returning the *same* `InvalidCredentials` message:
   ```csharp
   _ = _userManager.PasswordHasher.VerifyHashedPassword(new ApplicationUser(), DummyPasswordHash, request.Password);
   return AuthResult.Fail(AuthError.InvalidCredentials, "Invalid email or password.");
   ```
   Without it, "no such account" returns measurably faster than "wrong password" — a **timing oracle** that lets an attacker enumerate registered emails. (Note the asymmetry: `/register` *does* disclose "email already taken" for UX — an accepted tradeoff documented in the code; login is the hardened path.)

2. **Single-use rotation.** `IssueTokensAsync(user, ct, replacing: stored)` mints a new pair, persists the new token's hash, and on the predecessor stamps `RevokedAt`, `ReasonRevoked = "rotated"`, and `ReplacedByTokenHash` — chaining old to new in one save.

3. **Reuse detection.** Presenting an *already-revoked* token is the fingerprint of a stolen, replayed token. The response is a **global logout**: revoke *every* live token for that user, log a warning, fail. The thief's copy and all live sessions become worthless at once.

4. **Lockout** is honoured via `CheckPasswordSignInAsync(..., lockoutOnFailure: true)` (5 failures → 15-minute lock, configured in `Program.cs`).

One honest caveat the author left in a comment: rotation is read-then-write without a concurrency token, so two requests racing the *same* token could each mint a successor. Browsers serialize refresh on the one HttpOnly cookie, so a normal client can't trigger it; a rowversion'd conditional update is tracked as a follow-up.

#### `Domain/Entities/RefreshToken.cs` + `RefreshTokenConfiguration.cs`

**Only the hash is stored** — `TokenHash` is the SHA-256 of the cookie's opaque token, so a DB leak yields no usable credential. The lifecycle columns (`RevokedAt`, `ReplacedByTokenHash`, `ReasonRevoked`) make every token revocable and rotation auditable. The config puts a **unique index on `TokenHash`** (the lookup key) and a **non-unique index on `UserId`** (the reuse-revocation query), with a cascade delete so a deleted user's tokens go too. It implements `IAuditableEntity`, so it's the first entity to actually exercise the Phase 0 `AuditingInterceptor`.

#### `Repositories/RefreshTokenRepository.cs`

Pure persistence — the rotation/reuse *policy* stays in `AuthService`. One subtle line worth noting: `ListNotRevokedByUserAsync` filters on `RevokedAt == null` only (not expiry). Re-revoking an already-expired token is harmless, and avoiding a `DateTimeOffset >` comparison keeps the query **provider-portable** so the SQLite-backed unit tests can translate it.

#### `Middlewares/CsrfMiddleware.cs`

Conventional middleware that 403s any non-safe method (`GET/HEAD/OPTIONS/TRACE` pass through). The check requires the `csrf` cookie and `X-CSRF-Token` header to be present, **equal** (constant-time), **and** a validly-signed token:

```csharp
bool valid = !string.IsNullOrEmpty(cookie) && !string.IsNullOrEmpty(header)
    && SecureTokens.FixedTimeEquals(cookie, header) && csrf.Validate(cookie);
```

It resolves `ICsrfTokenService` via **method injection** on `InvokeAsync`. It's defense-in-depth *on top of* `SameSite=Strict` (the primary defense — a cross-site request never carries the auth cookie at all); the bootstrap `GET /auth/csrf` is a safe method, so it's exempt and can hand the SPA its first token. On failure it writes the standard `ApiResponse` envelope (`CSRF_VALIDATION_FAILED`).

#### `Controllers/AuthController.cs`

The HTTP boundary. Every endpoint is `[AllowAnonymous]` except `me` (`[Authorize]`), all under `api/v1/auth`. Its job is narrow: run the FluentValidator (returning a 422 envelope on failure), call the service, then `HandleAuthResult` — on success, write the access/refresh/CSRF cookies and return *only* `AuthUserDto`; on failure, map the typed `AuthError` to (status, code). **The success body never carries a token** — that's the whole point of ADR-0007. Note `MapError` returns `423 Locked` for `LockedOut` and `409` for `EmailAlreadyTaken`; `refresh` clears all cookies on any failure so the client falls cleanly back to login.

#### `Identity/AuthResult.cs`

Auth failures are modeled as a typed `AuthResult` (an `AuthError` enum + an `AuthTokens` record), **not exceptions** — "wrong password" and "email taken" are *expected control flow*, and throwing for them would be both slower and semantically wrong. The enum doc itself encodes the security stance: `InvalidCredentials` is "deliberately indistinguishable to the caller."

#### `Identity/IdentityDataSeeder.cs` + `Common/Constants/Roles.cs`

`Roles` is the single source of truth for the four RBAC names (`Customer`, `Staff`, `StoreManager`, `Administrator`) — referenced by the seeder, `[Authorize(Roles=...)]`, and registration, so a typo can't silently slip through and fail authz at runtime. The seeder is **idempotent** (creates only what's missing), runs in a DI scope after the host is built, and **skips admin creation with a warning** if `Auth:DefaultAdmin:Password` is blank rather than seeding a guessable account. It logs the admin's *id*, never the email (a sensitive field).

#### `Identity/JwtOptions.cs` / `CsrfOptions.cs` / `AuthSettings.cs` and the `Program.cs` wiring

The auth config is bound to typed options, and the two HMAC keys are **separate by design**: `Jwt:Key` signs the server's own access tokens; `Csrf:Key` signs the token the client echoes back. **Key separation** lets the two purposes rotate independently — leaking/rotating one never forces the other. Both are `.Validate(... Length >= 32).ValidateOnStart()`, so a short or missing key **fails the boot**, not a request. `AuthSettings.SecureCookies` defaults to `true` so a missing config never silently downgrades production.

Three `Program.cs` decisions are interview-grade:

- **`AddIdentityCore`, not `AddIdentity`.** `AddIdentity` registers four authentication *cookie* schemes and sets them as the default authenticate/challenge schemes — which then fight your Bearer default and 401 every `[Authorize]`. `AddIdentityCore` registers none of that, leaving Bearer the sole uncontested scheme; you then re-add only the pieces a token flow uses: `AddRoles` → `AddEntityFrameworkStores` (must come *after* roles) → `AddSignInManager` → `AddDefaultTokenProviders`.
- **JWT from a cookie.** A `JwtBearerEvents.OnMessageReceived` hook pulls the token out of the `access_token` cookie before validation; if absent, the default `Authorization`-header extraction still runs (so Swagger's Authorize box works). All four `TokenValidationParameters` validations (`ValidateIssuer/Audience/Lifetime/IssuerSigningKey`) are **ON** — disabling any is the classic JWT CVE.
- **Single source of truth for the key.** `JwtBearerOptions` is configured from the injected `IOptions<JwtOptions>` — the same object `JwtService` mints with — so minting and validation can *never* diverge, even when an integration test layers in-memory config after startup.

Lifetimes: `JwtService` and `CsrfTokenService` are **singletons** (immutable); `AuthService`, the repository, and the seeder are **scoped** (they touch the scoped `DbContext`).

### Story 1.1 — what to know cold

1. **Token placement is the whole thesis (ADR-0007).** Access JWT → HttpOnly cookie (XSS can't read it). Refresh token → opaque random, **only its SHA-256 hash persisted** (DB leak yields nothing). The response body carries *only* `AuthUserDto`, never a token.
2. **Refresh rotation is single-use with reuse detection.** Each refresh revokes the old token (`rotated`) and links it to its successor; presenting an already-revoked token triggers a **global logout** of the user's whole live set.
3. **CSRF is signed double-submit.** `{random}.{HMAC(random)}` validated against a **dedicated `Csrf:Key`** — the signature defeats cookie-planting attacks that naive double-submit can't, layered on top of `SameSite=Strict`.
4. **`AddIdentityCore`, not `AddIdentity`** — for a token API, `AddIdentity` would register competing cookie schemes that 401 every `[Authorize]`. Bearer is the only scheme; the JWT is extracted from a cookie via a `JwtBearerEvents` hook.
5. **Login is non-enumerable; the dummy PBKDF2 hash equalizes timing** so "no such account" can't be told from "wrong password" by latency — and the uniform `InvalidCredentials` message hides which failed.
6. **The service is HTTP-agnostic by design.** `AuthService` returns a typed `AuthResult` and never touches `HttpContext`; the controller alone turns that into Set-Cookie headers — which is what makes every auth path unit-testable without a web host.

---

## 3. Story 1.2 — Catalog (products, variants, inventory, image upload)

### What is in `src/api/Retail.Api/` (the catalog slice)

```
Retail.Api/
├─ Domain/Entities/
│  ├─ Category.cs                          ← self-referencing tree (depth ≤ 3), soft-deletable
│  ├─ Product.cs                           ← catalogue product; owns variants; soft-deletable
│  ├─ ProductVariant.cs                    ← the purchasable unit: price + options + 1:1 stock
│  └─ InventoryItem.cs                      ← 1:1 stock row; RowVersion concurrency token
├─ Data/
│  ├─ RetailDbContext.cs                   ← 4 new DbSets + soft-delete global query filters
│  ├─ Configurations/
│  │  ├─ CategoryConfiguration.cs          ← filtered unique slug index, Restrict FK
│  │  ├─ ProductConfiguration.cs           ← filtered unique Sku/Slug, composite list index
│  │  ├─ ProductVariantConfiguration.cs    ← Options↔OptionsJson ValueConverter + comparer
│  │  └─ InventoryItemConfiguration.cs     ← IsRowVersion(), Ignore(Available), 1:1 cascade
│  └─ Migrations/20260614093737_0002_catalog.cs  ← 4 tables + the filtered indexes
├─ Repositories/
│  ├─ IProductRepository.cs / ProductRepository.cs   ← read (AsNoTracking) + write loads
│  └─ ICategoryRepository.cs / CategoryRepository.cs  ← list, exists, parent-walk lookups
├─ Services/
│  ├─ ICatalogService.cs                   ← the public surface (reads + admin writes)
│  └─ CatalogService.cs                    ← all the business rules live here
├─ Controllers/CatalogController.cs        ← /api/v1/catalog; anon reads + Administrator writes
├─ DTOs/
│  ├─ Requests/  Create/Update Product, Category, Variant + ProductListQuery
│  └─ Responses/ ProductSummaryDto, ProductDetailDto, ProductVariantDto, CategoryDto
├─ Mappers/CatalogMappers.cs               ← explicit entity→DTO; "from" price + StockStatus
├─ Validators/  Create/Update Product, Category, Variant (FluentValidation, shape-only)
├─ Common/
│  ├─ Models/PagedResult.cs                ← page payload + paging metadata
│  └─ Helpers/{Slug,ProductImage}.cs       ← slugifier + image rules/magic-byte sniff
├─ Storage/
│  ├─ BlobStorageOptions.cs                ← bound config: container, conn string, public toggle
│  ├─ IBlobStorageClient.cs                ← "upload/delete a blob" abstraction
│  └─ BlobStorageClient.cs                 ← Azure.Storage.Blobs impl (Azurite in dev)
├─ Exceptions/{NotFoundException,ConflictException}.cs  ← domain exceptions → 404/409
└─ Middlewares/ExceptionMiddleware.cs      ← maps those exceptions to the envelope
```

### Per-file purpose

#### `Domain/Entities/Product.cs` + `ProductVariant.cs`

The split is the single most important modelling decision in the story. `Product` carries the **marketing shell** — name, slug, SKU, description, SEO fields, brand, `CategoryId`, `IsPublished`, `PrimaryImageBlobKey`. It carries **no price and no stock**. Those live on `ProductVariant`, which is *the purchasable unit*: its own `Sku`, an `Options` dictionary (`{ "size": "M" }`), `PriceCents`, optional `CompareAtPriceCents`, `IsActive`, and a 1:1 `Inventory`.

**Why split a "product" from a "variant"?** Because a customer buys "the medium red one," not "the shirt." Price and stock are properties of the specific SKU you can put in a cart, not of the abstract product. Folding them onto `Product` would force one price and one stock count across every size/colour — wrong the moment a product has two sizes.

**Money is `int PriceCents`, never `decimal`.** (resume-gold) The XML doc says it outright: *"Money is never `decimal` on hot tables."* Integer cents are exact (no binary-float rounding), compare and sum without surprises, and serialize cleanly to JSON. You format to "$12.99" at the very edge (the UI). Storing `12.99m` invites a `0.1 + 0.2` class of bug in totals.

#### `Domain/Entities/InventoryItem.cs`

A 1:1 stock row per variant with `OnHand`, `Reserved`, and a **computed** `Available => OnHand - Reserved`. It carries a `byte[] RowVersion`.

**Why is `Available` a computed C# property, not a column?** A stored "available" would be a second source of truth that drifts the instant `OnHand` or `Reserved` changes without it. Derive it; never persist it. The configuration's `builder.Ignore(i => i.Available)` keeps EF from trying to map it.

**Interview gotcha:** the `RowVersion` is here in Story 1.2 but the optimistic-concurrency *checkout* paths that consume it don't land until Phase 2. Why model it now? Because adding a concurrency token to a hot table later is a migration on a live table; baking it in at table-creation time is free. Inventory is the textbook concurrency-sensitive table — two buyers racing the last unit — so it gets the token from day one.

#### `Domain/Entities/Category.cs`

A self-referencing tree: `ParentId` + `Parent` + `Children`. Soft-deletable via `IsDeleted`. Max depth (3) is **not** in the entity — it's enforced in the service.

**Why enforce depth in the service, not the schema?** SQL has no clean declarative "this tree is at most 3 deep" constraint. A recursive CTE check trigger would be opaque and slow. The service walks the parent chain in plain C# (`EnsureParentDepthAsync`) where the rule is readable and testable.

#### `Data/Configurations/ProductVariantConfiguration.cs` — the JSON ValueConverter (resume-gold)

The `Options` dictionary is stored as a single `OptionsJson` `nvarchar(max)` column via a `ValueConverter<Dictionary<string,string>, string>` — serialize on write, deserialize on read. Crucially it *also* supplies a `ValueComparer`:

```csharp
builder.Property(v => v.Options)
    .HasColumnName("OptionsJson")
    .HasColumnType("nvarchar(max)")
    .IsRequired()
    .HasConversion(converter, comparer);
```

**Why the comparer is not optional.** EF's change tracker decides "did this property change?" by reference equality for reference types by default. A `Dictionary` mutated in place (`options["size"] = "L"`) is the *same reference* — EF would miss the change and silently skip the UPDATE. The comparer tells EF to compare by **serialized form**, so in-place mutations are detected. Omit it and EF emits a model warning and loses edits.

**Why JSON-in-a-column instead of an `OptionValues` child table?** Variant options are read-and-display only here — never queried or joined on. A schemaless JSON blob is the pragmatic fit; normalizing into a key/value table buys nothing until you need to filter "all red variants" (faceted search, a later phase).

#### `Data/Configurations/*Configuration.cs` — soft-delete + filtered unique indexes (resume-gold)

Every "unique" index on a soft-deletable entity is a **filtered** unique index:

```csharp
builder.HasIndex(p => p.Slug)
    .IsUnique()
    .HasFilter("[IsDeleted] = 0")
    .HasDatabaseName("UX_Product_Slug");
```

**Why `WHERE [IsDeleted] = 0`?** Soft delete means deleted rows stay in the table. A plain unique index on `Slug` would then forbid ever reusing the slug of a deleted product — delete `acme-shoe`, and you can never create `acme-shoe` again. The filter scopes uniqueness to *live* rows only, so a deleted slug/SKU is free to reuse. This is the pairing that makes soft delete actually usable. `Product` filters `Sku` and `Slug`; `Category` filters `Slug`. `ProductVariant.Sku` is **globally** unique (not filtered) because variants are hard-deleted, not soft-deleted.

Two more configuration details worth knowing cold: FKs into `Category` use `DeleteBehavior.Restrict` (you can't hard-delete a category out from under its products/children — you soft-delete instead), while `Product→Variants` and `Variant→Inventory` use `DeleteBehavior.Cascade` (deleting a product takes its variants and their stock with it). And `ProductConfiguration` adds a composite `IX_Product_CategoryId_IsPublished` ordered category-first — the equality predicate the storefront listing filters on.

#### `Data/RetailDbContext.cs` — the soft-delete global query filters

`OnModelCreating` adds two lines after the configuration scan:

```csharp
builder.Entity<Product>().HasQueryFilter(p => !p.IsDeleted);
builder.Entity<Category>().HasQueryFilter(c => !c.IsDeleted);
```

**Why a global filter instead of `.Where(p => !p.IsDeleted)` in every query?** Because forgetting it once leaks deleted data. A global filter means *every* LINQ query against `Products`/`Categories` silently excludes deleted rows — repositories don't have to remember. An admin "show deleted" view would opt back in with `IgnoreQueryFilters()`. This is the half of soft delete the filtered index doesn't cover: the index makes reuse legal, the filter makes deleted rows invisible.

#### `Repositories/{Product,Category}Repository.cs` — the read/write split

`ProductRepository` has two flavours of method, and the difference is load-bearing:

- **Read paths** (`ListPublishedAsync`, `GetPublishedDetailBySlugAsync`, `ListForAdminAsync`, `GetDetailByIdAsync`) use `AsNoTracking()` — no change tracker overhead for data you're only serializing out.
- **Write paths** (`GetByIdForWriteAsync`) deliberately **track** the entity so the service can mutate it and `SaveChangesAsync()` generates the UPDATE.

Note the two listing methods are near-twins: `ListPublishedAsync` adds `.Where(p => p.IsPublished)`; `ListForAdminAsync` omits it. **Why two methods and not a boolean flag?** Because "anonymous customers see only published" vs "admins see drafts too" is an authorization boundary, and an endpoint-level method makes that boundary impossible to call wrong by passing the wrong flag.

**Interview gotcha:** search is `p.Name.Contains(search)` → SQL `LIKE '%term%'`. A leading-wildcard LIKE is **non-sargable** — it can't use an index, so it's a full table scan. Fine for a portfolio catalogue; the service even drops 1-char terms to avoid scanning on near-empty input, and the comment flags SQL Server full-text `CONTAINS` as the real-scale answer. Naming this tradeoff unprompted is the interview win.

#### `Services/CatalogService.cs` — where every business rule lives (resume-gold)

This is the heart of the story and the reason the three-tier split exists. The controller does HTTP; the repository does SQL; **the service does the thinking**:

- **Slug resolution** — `ResolveSlug` takes the explicit slug or derives one from the name via `Slug.From`, and rejects input that yields an empty slug.
- **Uniqueness pre-checks** — `EnsureProductSkuAndSlugFreeAsync` throws `ConflictException` *before* hitting the DB constraint, so the client gets a clean 409 with a readable message instead of a raw `DbUpdateException`.
- **Category depth** — `EnsureParentDepthAsync` walks the parent chain and rejects nesting past `MaxCategoryDepth = 3`.
- **Paging hygiene** — `Math.Max(1, page)` and `Math.Clamp(pageSize, 1, 100)` defend against `page=0` and `pageSize=10000`.
- **Variant + stock co-creation** — `AddVariantAsync` builds the `ProductVariant` *with* its `InventoryItem { OnHand = request.InitialStock }` inline, so the 1:1 stock row is born with the variant in one `SaveChanges`.

**Why throw exceptions for "expected" failures (duplicate SKU, missing product)?** It keeps the happy path linear (`?? throw new NotFoundException(...)`) and centralizes the HTTP mapping in one middleware instead of threading `Result<T>` types through every layer. The cost — exceptions for control flow — is acceptable because these are genuinely exceptional request outcomes, not inner-loop logic.

**Interview gotcha — the image-swap ordering** in `SetProductPrimaryImageAsync`: it uploads the new blob under a **fresh** key, commits the DB pointer, *then* best-effort-deletes the old blob inside a `try/catch` that only logs on failure. The order is deliberate: never delete the old image before the new pointer is durably saved, or a crash mid-operation leaves a product pointing at a blob that no longer exists. A failed cleanup just leaves an orphan blob — recoverable; a dangling DB reference is a broken page.

#### `Controllers/CatalogController.cs` — anon reads vs Administrator writes

The whole controller is `/api/v1/catalog`. The authorization story is per-action:

- Public reads — `[AllowAnonymous]` on `GET products`, `GET products/{slug}`, `GET categories`.
- Admin everything else — `[Authorize(Roles = Roles.Administrator)]` on every write **and** on the admin read endpoints (`admin/products`, `admin/products/{id}`) that expose unpublished drafts.

**Why route `{slug}` for the public detail but `{id:guid}` for the admin detail?** Customers navigate by human-readable, SEO-friendly slug; admins edit a specific row by stable id. The `:guid` route constraint also means a non-GUID id never even reaches the action — it 404s at routing.

Validation is run explicitly via a private `ValidateAsync` helper that returns a 422 `ApiResponse.Fail(...)` with per-field `ApiError`s, or `null` to continue — rather than relying on `[ApiController]`'s automatic 400, so validation failures wear the *same envelope* as everything else.

#### `Controllers/CatalogController.cs::UploadProductImage` — defense in depth (resume-gold)

The image endpoint stacks **four** independent checks, and the layering is the point:

```csharp
[RequestSizeLimit(ProductImage.MaxBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = ProductImage.MaxBytes)]
```

1. `[RequestSizeLimit]` / `[RequestFormLimits]` reject an oversized body **at the framework edge, before model binding buffers it** — so a 1 GB upload is killed at the socket, not after it's in memory.
2. A post-bind `file.Length > MaxBytes` check (belt-and-suspenders).
3. `ProductImage.IsAllowedContentType(file.ContentType)` — a *fast early reject* on the declared type.
4. **Magic-byte sniffing** via `ProductImage.TryDetectContentType` on the first 12 bytes — and the blob is stored with the **detected** type, never the client-declared one.

**Interview gotcha:** why sniff bytes when you already checked `Content-Type`? Because the client-supplied `Content-Type` is **spoofable** — an attacker renames `evil.html` to `evil.jpg` and sets `image/jpeg`. The magic-byte check is the *authoritative* one; it reads the real `FF D8 FF` (JPEG) / `89 50 4E 47` (PNG) / `RIFF…WEBP` signatures. Trusting the declared type is a classic content-sniffing/stored-XSS vector.

#### `Common/Helpers/Slug.cs` + `ProductImage.cs`

`Slug.From` lower-cases, collapses every run of non-alphanumerics to a single hyphen via a `[GeneratedRegex]`, and trims stray hyphens — `"Acme Running Shoe!"` → `"acme-running-shoe"`. **Why `[GeneratedRegex]` (source-generated) over `new Regex(...)`?** The pattern is compiled at build time, so there's no per-call regex-compilation cost and no runtime allocation. `ProductImage` is the single home for the upload rules — `MaxBytes = 5 MB`, the content-type↔extension table, and the magic-byte detector — so the cap and the allow-list live in exactly one place.

#### `Storage/BlobStorageClient.cs` + `BlobStorageOptions.cs` — private-by-default

`IBlobStorageClient` is a two-method abstraction (`UploadAsync`, `DeleteAsync`) so the service depends on "store a blob," not on the Azure SDK — testable and swappable. The implementation has two details worth knowing:

- **Private by default.** `PublicReadImages` defaults to `false` → containers are created with `PublicAccessType.None`. Production stays non-anonymously-readable unless explicitly opted in; dev/Azurite flips it `true` so the storefront can fetch images by direct URL.
- **`Lazy<BlobServiceClient>`.** The client is built on first use, never in the constructor. **Why?** This is a singleton; resolving it for an ordinary catalogue request must not touch the (possibly blank) connection string. Lazy means a bad conn string only fails an actual blob op, not every catalogue read — while still reusing one pooled client per Azure SDK guidance.

**Interview gotcha — the blob key scheme:** `products/{productId:N}/{Guid.NewGuid():N}.{ext}`. Every upload gets a **fresh GUID**, so uploads never overwrite — which is exactly what lets the service swap-then-delete safely and sidesteps CDN cache-busting (a new key is a new URL). The pinned `BlobClientOptions.ServiceVersion` is there because the SDK's newest default version isn't recognised by Azurite and returns 400.

#### `Mappers/CatalogMappers.cs` — explicit mapping, derived fields

Hand-written extension methods (`product.ToDetailDto()`), no AutoMapper (a CODING_STANDARDS decision — explicit mapping is greppable and debuggable). Two derived fields are computed here, not stored:

- **"From" price** — `ProductSummaryDto.FromPriceCents` is the **min `PriceCents` of active variants**, or `null` if none are active. That's the "from $12.99" on a grid card.
- **`StockStatus`** — `StockStatusFor(available)`: `<= 0` → `"OutOfStock"`, `< 10` → `"LowStock"`, else `"InStock"`. The `10` threshold traces to REQUIREMENTS §2.1 ("Low Stock < 10").

**Why compute these in the mapper, not store them?** They're projections of live data (variant prices, current stock). Storing them is denormalization that goes stale the instant stock moves.

#### `Common/Models/PagedResult.cs`

A generic page payload: `Items`, `Page`, `PageSize`, `TotalCount`, plus **computed** `TotalPages`, `HasNext`, `HasPrevious`. It rides inside `ApiResponse<PagedResult<T>>` like any payload.

**Why ship `HasNext`/`TotalPages` instead of letting the client compute them?** The server already knows `TotalCount` from its `CountAsync`; sending the derived flags means the frontend renders pagination controls without re-deriving (and possibly mis-deriving) paging math.

#### `Exceptions/{NotFoundException,ConflictException}.cs` + how `ExceptionMiddleware` maps them

Two tiny domain exceptions. The service throws them by intent; the middleware turns them into the envelope. The relevant cases in `ExceptionMiddleware`'s switch:

```csharp
NotFoundException => (StatusCodes.Status404NotFound, "NOT_FOUND", ex.Message),
ConflictException => (StatusCodes.Status409Conflict, "CONFLICT", ex.Message),
```

**Why custom exceptions over returning status codes from the service?** It keeps the service HTTP-agnostic — it speaks "not found" / "conflict," and the middleware (the one layer that *is* about HTTP) owns the status-code mapping. Note these surface `ex.Message` to the client (e.g. "A product with slug 'acme-shoe' already exists.") because the messages are deliberately written to be safe and user-facing — unlike the generic 500 path, which hides the real exception.

### Story 1.2 — what to know cold

1. **Product vs Variant is the core model.** `Product` is the marketing shell; `ProductVariant` is the purchasable unit that carries `PriceCents`, `Options`, and a 1:1 `InventoryItem`. Money is **integer cents**, never `decimal`.
2. **Soft delete = two mechanisms working together.** A **global query filter** (`HasQueryFilter(p => !p.IsDeleted)`) hides deleted rows from every query; a **filtered unique index** (`HasFilter("[IsDeleted] = 0")`) scopes uniqueness to live rows so a deleted slug/SKU can be reused. You need both.
3. **Three tiers, clean responsibilities.** Controller = HTTP + authz + validation; Repository = SQL with `AsNoTracking` reads vs tracked writes; **Service = every business rule** (slug resolution, uniqueness pre-checks, category depth, paging clamps, variant+stock co-creation).
4. **Reads are anonymous and published-only; writes (and draft reads) require `Administrator`.** Two separate repository methods enforce the published-vs-all boundary instead of a spoofable flag.
5. **Image upload is defense in depth.** Edge size limit (pre-buffer) → post-bind size check → declared-content-type fast reject → **authoritative magic-byte sniff**, stored under a fresh-GUID blob key with the *detected* type. Blobs are **private by default** (`PublicReadImages=false`); swap-then-delete never leaves a dangling pointer.
6. **The `Options` dictionary needs a `ValueComparer`, not just a `ValueConverter`.** Without the comparer, EF misses in-place dictionary mutations and silently skips the UPDATE — and `StockStatus`/"from" price are computed in the mapper, never stored.

---

## 4. Story 1.3 — Frontend (storefront + admin + auth UI)

Phase 0 left you a React shell with one placeholder route. Story 1.3 fills it in: a public **storefront** (browse + detail), an **admin product manager** (table + create/edit/variants/image), and the **auth UI** (login + register + a session bootstrap that runs on every page load). Every API call goes through the Phase-0 typed client; every write rides the CSRF middleware. This is the story that turns "wired" into "working."

### What is in `src/web/src/features/storefront`

```
features/storefront/
├─ CatalogPage.tsx              ← URL-driven grid: page + category + search live in the query string
├─ ProductDetailPage.tsx        ← by-slug detail; variant picker drives the displayed price/stock
├─ components/
│  ├─ FilterPanel.tsx           ← search box + category <Select> (shared with the admin table)
│  ├─ ProductCard.tsx           ← grid tile, links to /products/:slug, shows "From $X"
│  ├─ ProductImage.tsx          ← <img> or a "No image" placeholder; lazy-loaded
│  ├─ StockBadge.tsx            ← maps the API stock-status string → a coloured Badge
│  └─ VariantSelector.tsx       ← native <select> over variants; inactive ones disabled
└─ hooks/
   ├─ useProductsQuery.ts       ← paged published list — GET /catalog/products
   ├─ useProductQuery.ts        ← one product by slug — GET /catalog/products/{slug}
   └─ useCategoriesQuery.ts     ← category list for the filter dropdown
```

### Per-file purpose

#### `features/storefront/CatalogPage.tsx`

The catalogue grid. The one idea to absorb: **the URL is the source of truth for list state**. `page`, `category`, and `q` live in the query string via `useSearchParams`, not in `useState`. That makes every list view **shareable, bookmarkable, and back-button-correct** for free — and it pairs perfectly with TanStack Query because the query key is derived from those same params.

The `setParam` helper encodes the one rule worth stating out loud: **filter changes reset to page 1, page changes preserve filters.** Otherwise you land on "page 5 of a search that only has 2 pages" and see nothing.

```ts
function setParam(key, value, resetPage) {
  setSearchParams((prev) => {
    const next = new URLSearchParams(prev)
    value ? next.set(key, value) : next.delete(key)
    if (resetPage) next.delete('page')
    return next
  })
}
```

Note the three render states — `isError` / `isLoading` (skeleton grid) / data — come straight off the query. No manual loading boolean.

#### `features/storefront/hooks/useProductsQuery.ts` (resume-gold)

A TanStack Query hook over the generated client. Two things to know cold.

First, **the query key carries the params**: `queryKey: ['products', params]`. When the user pages or filters, the key changes, TanStack treats it as a different query, and caches each combination separately — flip back to a previous page and it's instant from cache.

Second — the **PascalCase query-param gotcha**, the single most surprising thing in the whole frontend:

```ts
query: { Page: params.page, PageSize: params.pageSize, CategoryId: ..., Search: ... }
```

**Interview gotcha:** the request *body* and the *response* are camelCase (Program.cs sets a camelCase JSON policy), but **query-string params are PascalCase**. Why the split? ASP.NET's JSON serializer governs bodies and responses; query parameters are bound by **model binding**, which matches on the C# property name (`Page`, `PageSize`) and is unaffected by the JSON naming policy. Send `page=2` and it silently binds to the default — no error, just wrong results. The comment in the file exists so future-you doesn't lose an afternoon to it.

#### `features/storefront/ProductDetailPage.tsx`

Loads one product by slug, then keeps a local `selectedVariantId` in `useState`. The price, compare-at strikethrough, and `StockBadge` all read off `selected` (the chosen variant, defaulting to the first). **Why variant-level price, not product-level?** Because a "Large" can cost more than a "Small" — price and stock live on the variant in this domain, so the UI must too. This is also the dividing line you can articulate in an interview: **the product is the "what," the variant is the buyable unit.**

#### `features/storefront/components/StockBadge.tsx` + `FilterPanel.tsx`

`StockBadge` is a tiny lookup table mapping `InStock` / `LowStock` / `OutOfStock` to a `Badge` variant — the kind of presentational glue that keeps colour decisions in one place instead of sprinkled through JSX. `FilterPanel` is deliberately **shared by both the storefront and the admin table** (the admin page imports it), so search-and-filter behaves identically in both surfaces.

### What is in `features/admin`

```
features/admin/
├─ AdminHomePage.tsx            ← dashboard placeholder; links to product management
├─ AdminProductsPage.tsx        ← admin table — hits /catalog/admin/products (drafts included)
├─ ProductFormPage.tsx          ← create+edit; one :id param decides the mode
├─ components/
│  ├─ ProductForm.tsx           ← RHF + zod product fields form
│  ├─ ImageUploadField.tsx      ← multipart primary-image upload
│  └─ VariantsSection.tsx       ← variant table + inline add-form (useFieldArray)
├─ hooks/
│  ├─ useAdminProductsQuery.ts  ← admin paged list + the adminProductKeys factory
│  ├─ useAdminProductQuery.ts   ← one product by id (loads drafts) for the edit form
│  ├─ useProductMutations.ts    ← create / update / delete / image upload + invalidation
│  └─ useVariantMutations.ts    ← add / delete variant + invalidation
└─ lib/
   └─ product-schema.ts         ← zod schemas (mirror the backend) + form→API mappers
```

#### `features/admin/AdminProductsPage.tsx`

Same URL-driven pattern as the storefront, pointed at a **different endpoint**: `/catalog/admin/products`, which returns **unpublished drafts too** so admins can manage work-in-progress. The `Published`/`Draft` `Badge` reflects storefront visibility at a glance. Delete is a **soft delete** behind a `window.confirm` — the copy literally says "recoverable," because the backend tombstones rather than hard-deletes.

#### `features/admin/lib/product-schema.ts` (resume-gold)

This file is the heart of the admin work and the best interview artifact in the story. Two responsibilities, both deliberate.

**1. zod schemas that mirror the backend FluentValidation rules.** SKU ≤ 64, Name ≤ 200, Slug ≤ 160, etc. — the same numbers as `Validators/*.cs`. The backend is still the source of truth (it returns 422), but echoing the rules client-side gives **instant inline feedback instead of a round-trip per typo**. The honest framing for an interview: *"validate twice — client for UX, server for trust; the client copy can never be the gate."*

**2. The dollars-to-cents form boundary.** Money is **integer cents end-to-end** (DB, API, store) — never a float, so you never accumulate `$0.01` rounding drift. The *one* place a human types dollars is a form input, so the conversion happens here and nowhere else:

```ts
priceCents: dollarsToCents(values.priceDollars),   // "19.99" → 1999 (Math.round kills float drift)
```

The `toCreateProductBody` / `toUpdateProductBody` mappers also translate the form's "empty string" into the API's `null` (via `nullIfBlank`) — **the form speaks human, the API speaks data, and the translation lives at one testable seam.** Note `UpdateProductRequest` omits `sku` entirely: SKU is immutable after creation.

#### `features/admin/ProductFormPage.tsx`

One component, two modes, decided by a single route param: `/admin/products/new` (no `:id` → create) vs `/admin/products/:id` (→ edit). Two subtleties worth knowing:

- **Image upload and variants only appear in edit mode.** Both endpoints are keyed by an existing product id, so the create flow `navigate()`s into edit mode on success ("Now add variants and an image").
- **`key={productQuery.data?.id ?? 'new'}` on `<ProductForm>`.** React Hook Form snapshots `defaultValues` on mount and won't pick up async-loaded data. Changing the `key` **forces a fresh RHF instance** once the product loads, so the edit form actually shows the loaded values. Forget this and the edit form renders blank.

#### `features/admin/components/ProductForm.tsx` + `VariantsSection.tsx`

Both use **React Hook Form + `zodResolver`**. The why: RHF keeps inputs **uncontrolled** (`register()` attaches a ref per field — no re-render per keystroke), and `zodResolver` runs the shared schema on submit and surfaces inline `errors`. SKU renders `disabled` in edit mode, matching the immutability the mapper enforces.

`VariantsSection` adds **`useFieldArray`** for the variant's option rows (Size → M, Color → Red) — a dynamic, add/remove list of key/value pairs folded into the request's options map by `toOptionsMap`. It runs its **own RHF instance**, separate from the product form, so the two validate independently. (resume-gold: "dynamic field arrays with React Hook Form.")

#### `features/admin/hooks/useProductMutations.ts` (resume-gold)

The mutation layer, and where **TanStack Query cache invalidation** earns its keep. Every write invalidates the caches its change can affect, so the UI **refetches the truth instead of guessing it**:

```ts
queryClient.invalidateQueries({ queryKey: adminProductKeys.all })  // admin table (drafts)
queryClient.invalidateQueries({ queryKey: ['products'] })          // public storefront grid
```

**Why invalidate both?** A publish/price/image change is visible in two places — the admin table *and* the public grid — so both stale. Updates additionally invalidate `adminProductKeys.detail(id)` to refresh the edit page itself. The `adminProductKeys` factory (in `useAdminProductsQuery.ts`) centralises these keys so a mutation can't typo a key the query used.

**Interview gotcha — the multipart image upload.** openapi-fetch JSON-serializes bodies by default, which is wrong for a file. The fix is to override `bodySerializer` to build a `FormData` and return it — that makes the **browser set the multipart boundary `Content-Type` itself** (you must *not* set it by hand, or the boundary is missing). Because openapi-fetch skips the body entirely when it's `undefined`, the `File` is passed through a cast just to be truthy; `bodySerializer` is what actually runs.

The CSRF header on all these POST/PUT/DELETE calls is attached **automatically** by the client middleware — the mutation code never thinks about it.

### What is in `features/auth`

```
features/auth/
├─ LoginPage.tsx               ← email+password → POST /auth/login → applyAuthUser → redirect
├─ RegisterPage.tsx            ← RHF+zod (mirrors the password policy) → POST /auth/register
├─ SessionBootstrapper.tsx     ← runs once on mount: GET /auth/csrf then GET /auth/me
└─ session.ts                  ← applyAuthUser(): AuthUserDto → store, or clear on null
```

#### `features/auth/SessionBootstrapper.tsx` (resume-gold)

Wraps `<RouterProvider>` in `main.tsx` and runs **once on app load** in a `useEffect`. Two calls, in order:

```ts
await apiClient.GET('/api/v1/auth/csrf')   // 1. seed the double-submit CSRF cookie
const { data } = await apiClient.GET('/api/v1/auth/me')  // 2. who am I?
applyAuthUser(data?.data)                  // null on 401 → flips isLoading to false
```

**Why CSRF *first*?** State-changing requests need the `csrf` cookie present, and the browser only has it after the backend issues it — so you seed it before the user can click anything. **Why `/auth/me` at all?** The JWT is in an HTTP-only cookie JS can't read, so on a hard refresh the SPA has *no idea* who's logged in until it asks. A **401 is the normal "logged out" answer**, not an error — `.catch()` resolves it to `applyAuthUser(null)`, which flips the store's `isLoading` to false so `RoleGuard` stops blocking. The `cancelled` flag guards against a setState after unmount (and StrictMode's double-invoke in dev).

#### `features/auth/session.ts`

A four-line adapter, but it captures a real seam: it maps the generated `AuthUserDto` (every field optional in the schema) into the store's stricter `AuthUser`, coalescing nullables. It uses `useAuthStore.getState()` — **not** a hook — so it's callable from the bootstrap effect, event handlers, and the sign-out button alike.

#### `features/auth/LoginPage.tsx` + `RegisterPage.tsx`

Login is plain `useState` (two fields — RHF would be overkill); register uses **RHF + zod with a schema that mirrors the Identity password policy** (12+ chars, a letter, a digit). Both call `applyAuthUser(data.data)` on success — login redirects to the page the guard bounced you from (`location.state.from`, default `/admin`), register signs the new customer in and goes to `/account`.

### What is in the API layer + lib + new UI primitives

```
lib/
├─ api/
│  ├─ client.ts                ← openapi-fetch client + CSRF middleware (X-CSRF-Token)
│  ├─ types.ts                 ← ergonomic aliases over the generated schema
│  └─ schema.d.ts              ← generated by `pnpm gen:api` (do not hand-edit)
├─ format.ts                   ← formatCents + dollars↔cents helpers
├─ images.ts                   ← productImageUrl(): blob key → full URL (or null)
├─ utils.ts                    ← cn() (Phase 0)
└─ store/auth-store.ts         ← Zustand: { user, isLoading } (NO JWT)

components/ui/  (new in Phase 1)
├─ select.tsx                  ← styled native <select> (forwardRef)
├─ badge.tsx                   ← cva-driven status pill (success/warning/destructive)
├─ skeleton.tsx                ← pulsing load placeholder
├─ pagination.tsx              ← prev/next pager, hidden on single page
└─ textarea.tsx                ← shadcn multiline input (forwardRef)
```

#### `lib/api/client.ts`

The Phase-0 typed client, with the CSRF middleware now load-bearing. Two refinements landed this phase. The cookie/header names are **`csrf` → `X-CSRF-Token`** (the Phase-0 doc wrote `XSRF-TOKEN`; this is the real name — the rename was the "CSRF-name fix" in the commit log). And the middleware now **throws fast** when a state-changing request finds no CSRF cookie, instead of firing a guaranteed-403 round-trip:

```ts
if (!token) throw new Error('Missing CSRF token — please refresh the page and try again.')
```

`baseUrl: ''` is relative on purpose — Vite's dev proxy forwards `/api/*` to the backend, and in prod the SPA and API share an APIM origin, so **no environment-conditional URL code**. `credentials: 'include'` is what sends the HTTP-only JWT cookie at all.

#### `lib/api/types.ts` + `lib/images.ts` + `lib/format.ts`

`types.ts` is pure ergonomics: `ProductSummary` instead of `components['schemas']['ProductSummaryDto']` — one place to re-alias if a DTO is renamed. `images.ts` resolves a blob key to a full URL against `VITE_BLOB_BASE_URL` (Azurite in dev), returning `null` so `ProductImage` can render its placeholder. `format.ts` owns the money story (see the schema section above).

#### `components/ui/badge.tsx` + the other primitives

`Badge` follows the **Phase-0 shadcn recipe — cva for type-safe variants + `cn()` to merge** — adding `success`/`warning` colours the storefront and admin use for stock and publish state. `Select` and `Textarea` are intentionally **styled native controls** (`forwardRef` so RHF's `register()` can attach), not Radix — the comment notes Phase 3 can upgrade `Select` to Radix if the admin UI needs search/multi-select. `Skeleton` and `Pagination` are the load + paging glue every list view leans on.

### What is in app/ + layouts

#### `app/router.tsx`

A nested data-router: one `<StorefrontShell>` layout route wraps every page via `<Outlet />`. The interview-relevant decisions live in the comments:

- **Role names are `Administrator` / `StoreManager` / `Staff` / `Customer` — not `Admin`** (the Phase-0 doc's `'Admin'`/`'StoreManager'` placeholder is now the real seeded set).
- The **entire `/admin` surface is `Administrator`-only**, because the backend admin catalog endpoints are Administrator-only. The comment is explicit that StoreManager gets backed routes only when the Phase 3 RBAC matrix lands — **no dead-end UI for a role that has no endpoints yet.**
- `/account` is **`Customer`-only** (the `/profile` endpoints require it).
- React Router **ranks the static `new` segment above the `:id` param**, so `/admin/products/new` and `/admin/products/:id` both resolve correctly without ordering tricks.

#### `app/guards/RoleGuard.tsx`

Unchanged in spirit from Phase 0, and the why still matters most: **it is UX-only, never security.** Backend `[Authorize]` is the gate; this just prevents a flash of the wrong view. It returns `null` while `isLoading` (auth hydration in flight), `<Navigate to="/login" state={{ from: location }}>` for anonymous users (so login can route them back), and a plain redirect for the wrong role.

#### `components/layouts/StorefrontShell.tsx`

The auth-aware header. It reads the store with **selectors** (`useAuthStore((s) => s.user)`) so it only re-renders when its slice changes, and gates the Admin/Account links on role — but every link is guarded `!isLoading &&` so nothing flashes during bootstrap. Sign-out is `POST /auth/logout` then `applyAuthUser(null)`. Note it hardcodes the **same `Administrator`-only rule** as the router, so the header link and the route guard can never disagree.

### Story 1.3 — what to know cold

1. **List state lives in the URL, not `useState`.** `page`/`category`/`q` go through `useSearchParams`, which makes lists shareable and back-button-correct and feeds the TanStack query key — and the rule is *filter changes reset to page 1, page changes keep filters*.
2. **Query-string params are PascalCase; bodies and responses are camelCase.** Bodies obey the JSON naming policy; query params are bound by ASP.NET model binding on the C# property name. Wrong case binds silently to the default — no error.
3. **Mutations invalidate caches, they don't patch them.** Every write invalidates `adminProductKeys.all` (admin table) and `['products']` (storefront) so the UI refetches truth; the `adminProductKeys` factory keeps the keys honest.
4. **Money is integer cents end-to-end; dollars exist only inside a form.** `product-schema.ts` converts at that single boundary with `Math.round`, and also turns empty strings into API `null`s — the one testable seam between "human" and "data."
5. **Validate twice: zod on the client (mirrors FluentValidation) for instant UX, server for trust.** The client copy is never the gate — the backend's 422 is.
6. **Session bootstrap runs once on mount: `GET /auth/csrf` then `GET /auth/me`.** CSRF cookie first so writes work; `/auth/me` because the HTTP-only JWT is unreadable from JS, so a 401 is the normal "logged out" answer that flips `isLoading` false.
7. **RoleGuard (and the header) are UX-only; the backend is the gate.** The whole `/admin` surface is scoped to `Administrator` to exactly match the Administrator-only backend endpoints — no UI for roles without endpoints.

---

## 5. Story 1.4 — Customer profile + addresses

This is the first vertical slice where a logged-in **customer** acts on **their own domain data**. It is the cleanest place in the codebase to show you understand a subtle distinction interviewers probe: the **Identity user (authentication) is not the same thing as the customer profile (domain)** — and to defend two genuinely hard correctness problems (race-safe lazy creation, "one default per axis") with both a database guarantee and a service-layer transaction.

### What is in `Domain/`, `Data/`, `Repositories/`, `Services/`, `Controllers/`, `DTOs/`, `Mappers/`, `Validators/`

```
src/api/Retail.Api/
├─ Domain/Entities/
│  ├─ CustomerProfile.cs            ← 1:1 with ApplicationUser; DisplayName, Phone, Addresses
│  └─ Address.cs                    ← owned by a profile; two independent default flags
├─ Data/Configurations/
│  ├─ CustomerProfileConfiguration.cs ← 1:1 user FK (string), UX_CustomerProfile_AppUserId
│  └─ AddressConfiguration.cs       ← named HasIndex ×3, two FILTERED unique indexes
├─ Data/Migrations/
│  └─ 20260614215709_0003_customer_profile.cs ← creates CustomerProfile + Address tables
├─ Repositories/
│  ├─ ICustomerProfileRepository.cs ← owner-scoped reads + ExecuteUpdate default-clears
│  └─ CustomerProfileRepository.cs  ← EF impl; AsNoTracking GET path vs tracked write path
├─ Services/
│  ├─ ICustomerProfileService.cs    ← every method scoped to one appUserId
│  └─ CustomerProfileService.cs     ← lazy-create race-safe + clear-then-set in a tx
├─ Controllers/CustomerProfileController.cs ← /api/v1/profile, [Authorize(Roles=Customer)]
├─ DTOs/Requests/
│  ├─ UpsertProfileRequest.cs       ← DisplayName + Phone only (Email immutable)
│  └─ AddressRequest.cs             ← one shape for both create + update
├─ DTOs/Responses/
│  ├─ CustomerProfileDto.cs         ← profile + Email (threaded from the user) + addresses
│  └─ AddressDto.cs                 ← address as returned to its owner
├─ Mappers/CustomerProfileMappers.cs ← explicit entity→DTO, defaults-first ordering
└─ Validators/
   ├─ UpsertProfileRequestValidator.cs ← mirrors EF lengths + phone regex
   └─ AddressRequestValidator.cs    ← mirrors EF lengths + ISO-3166 alpha-2 regex
```

### Per-file purpose

#### `Domain/Entities/CustomerProfile.cs` (resume-gold)

The whole point of this entity is **separation of concerns**. ASP.NET Identity's `ApplicationUser` owns *authentication* — email, password hash, security stamp, lockout, 2FA phone. The customer's *domain* data — editable display name, a plain contact phone, saved addresses — lives on `CustomerProfile` so the two never entangle.

**Why not just add `Phone` to `ApplicationUser`?** Identity already inherits a `PhoneNumber` property, but it carries 2FA/confirmation semantics. "Where do we ship to" and "which number gets your login OTP" are different concepts that should not collide on one column.

The FK to the user is `AppUserId`, and it is deliberately a **`string`, not a `Guid`**:

```csharp
public string AppUserId { get; set; } = string.Empty;
```

**Interview gotcha:** Identity stores its primary key as a GUID *serialized to text* in `nvarchar(450)`. If you typed this FK as `Guid`, EF would emit a `uniqueidentifier` column and the FK to `AspNetUsers.Id` (an `nvarchar`) wouldn't match. The type has to mirror the principal column exactly.

One more design note worth saying out loud: `DisplayName` is the canonical editable value, but it is **mirrored back** onto `ApplicationUser.DisplayName` on every save. That keeps the lightweight `/auth/me` session path a single cheap query — the SPA never has to load the full profile just to render "Hi, Jane."

#### `Domain/Entities/Address.cs`

"Shipping vs billing" is modeled as **two independent booleans** (`IsDefaultShipping`, `IsDefaultBilling`), *not* a discrete `Type` column. One address can be the default for shipping, for billing, both, or neither. The invariant — **at most one default per axis per profile** — is the file's center of gravity and is enforced in two places (index + service). Unlike Product/Category/Review, addresses are **not soft-deletable**; removal is a hard delete.

#### `Data/Configurations/CustomerProfileConfiguration.cs`

Two load-bearing lines. The 1:1 relationship uses the typed-FK overload, and a **named unique index** enforces it at the database level:

```csharp
builder.HasOne(p => p.User).WithOne()
    .HasForeignKey<CustomerProfile>(p => p.AppUserId)
    .OnDelete(DeleteBehavior.Cascade);
builder.HasIndex(p => p.AppUserId).IsUnique()
    .HasDatabaseName("UX_CustomerProfile_AppUserId");
```

**Why the unique index when it's already a 1:1 mapping?** The 1:1 navigation is an EF *modeling* concept; the unique index is the *database* truth. It is exactly this index that makes lazy creation race-safe (below) — a second concurrent insert for the same user is rejected by SQL Server, not by hopeful app code.

#### `Data/Configurations/AddressConfiguration.cs` (resume-gold)

This is the most interview-worthy configuration in Phase 1. Three indexes all key off `CustomerProfileId`, so each must use the **named `HasIndex` overload**:

```csharp
builder.HasIndex(a => a.CustomerProfileId, "IX_Address_CustomerProfileId");
builder.HasIndex(a => a.CustomerProfileId, "UX_Address_DefaultShipping")
    .IsUnique().HasFilter("[IsDefaultShipping] = 1");
builder.HasIndex(a => a.CustomerProfileId, "UX_Address_DefaultBilling")
    .IsUnique().HasFilter("[IsDefaultBilling] = 1");
```

**Interview gotcha:** without distinct *names*, EF collapses three `HasIndex(a => a.CustomerProfileId)` calls into one index (last definition wins) because it dedupes by the indexed property set. The string name is what forces three separate indexes.

**Why a *filtered* unique index?** A plain unique index on `CustomerProfileId` would allow only one address per customer — wrong. The filter `[IsDefaultShipping] = 1` means the uniqueness constraint applies *only to rows where the flag is true*. So a profile can have ten addresses, but at most one with `IsDefaultShipping = 1`. The database itself rejects a second default. `Country` is also configured `IsFixedLength()` → `nchar(2)` (canonical ISO-3166 alpha-2), not `nvarchar`.

#### `Repositories/ICustomerProfileRepository.cs` + `CustomerProfileRepository.cs`

Pure data access — the business rules live in the service. Three things to know cold here.

First, the **GET path and write path are deliberately different**: `GetProfileReadOnlyAsync` uses `AsNoTracking()` (cheaper, no change-tracking overhead for a render), while `GetProfileAsync` returns a *tracked* graph for mutation. Both `.Include(p => p.User)` so the email can be threaded into the DTO.

Second, owner-scoping happens **in the query**, not after the fact:

```csharp
await _db.Addresses.Include(a => a.CustomerProfile)
    .FirstOrDefaultAsync(a =>
        a.Id == addressId && a.CustomerProfile!.AppUserId == appUserId, ct);
```

A `null` result means "doesn't exist **or** isn't yours" — both indistinguishable on purpose (see the 404 rule below).

Third, the `ClearDefaultShipping/BillingAsync` methods use **`ExecuteUpdateAsync`** — a single set-based `UPDATE` issued immediately, not a load-modify-save round trip. **Interview gotcha:** `ExecuteUpdate` bypasses `SaveChanges`, so it never runs through the `AuditingInterceptor`. That is why these methods take `updatedAt`/`updatedBy` parameters and **stamp the audit columns by hand** — otherwise the cleared rows would silently lose their audit trail.

#### `Services/CustomerProfileService.cs` (resume-gold — the centerpiece)

Three patterns make this file the interview anchor of the story.

**1. Race-safe lazy creation.** Profiles are created on first access, not at registration. The naive version ("check if exists, else insert") has a classic TOCTOU race: two tabs hit `GET /profile` at once, both see "no profile," both insert. The unique index on `AppUserId` guarantees one insert *fails* — and `CreateProfileAsync` turns that failure into a non-event instead of a 500:

```csharp
catch (DbUpdateException)
{
    _db.Entry(profile).State = EntityState.Detached;
    CustomerProfile? winner = await _repo.GetProfileAsync(appUserId, ct);
    if (winner is not null) return winner;   // the other request won — use its row
    throw;                                    // not the race we expected → rethrow
}
```

Detaching the losing entity and **re-reading** the winner makes lazy creation idempotent. Crucially, if no row appeared (i.e., the `DbUpdateException` was *not* the unique-index race), it rethrows rather than swallowing a real error — a subtlety worth pointing out.

**2. The "one default per axis" invariant, enforced clear-then-set inside a transaction.** Setting a new default address could momentarily produce *two* default-shipping rows, which the filtered unique index would reject. The service clears the prior default **first**, then writes the new one, both inside one `BeginTransactionAsync` so the change is atomic:

```csharp
await using var tx = await _db.Database.BeginTransactionAsync(ct);
await ClearSupersededDefaultsAsync(profile.Id, exceptAddressId: null, request, appUserId, ct);
await _repo.AddAddressAsync(address, ct);
await SaveDefaultChangeAsync(tx, ct);
```

The **ordering subtlety**: the clear is a `ExecuteUpdate` that hits the database *immediately* (it participates in the ambient transaction), while the new address is staged on the change tracker and flushed by the later `SaveChanges`. So by the time the index is checked at `SaveChanges`, the old default is already `false`. This belt-and-suspenders design is the talking point: **the filtered index is the hard guarantee; the clear-then-set is what keeps a legitimate change from tripping it.** And `SaveDefaultChangeAsync` still catches the SQL Server unique-violation error numbers — `catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })` — to translate a *concurrent* same-axis race into the project-standard 409, not a raw 500.

**3. The PII-never-logged rule.** Every log line uses only blessed-safe identifiers — the user id (a GUID) and the profile/address id. **No email, phone, or address field ever enters a log or an exception message**, because exception messages are surfaced to clients and, in Development, echoed alongside the stack trace. This is a one-sentence answer to "how do you handle PII?" that you can actually point at code for.

#### `Controllers/CustomerProfileController.cs`

`[Authorize(Roles = Roles.Customer)]` on `/api/v1/profile`. The defining design decision: **the user id comes from the auth cookie via `ICurrentUserAccessor`, never from the route or body.** There is structurally no way to address another user's profile — you can't pass an id you don't own because you never pass one at all.

This is also where the **404-not-403 rule** lives end to end. When you update or delete an address that isn't yours, the service throws `NotFoundException` → 404, *not* `ForbiddenException` → 403. **Why?** A 403 confirms "that address id exists, you just can't touch it" — an enumeration oracle leaking the existence of other users' data. A 404 reveals nothing. `TryGetUserId` also resolves the id defensively (a token missing the `NameIdentifier` claim returns 401, not a 500), and validation runs through a shared `ValidateAsync` helper returning a 422 envelope.

#### The DTOs, Validators, and `Mappers/CustomerProfileMappers.cs`

`AddressRequest` is **one shape for both POST and PUT** — there is no immutable field that would force a Create/Update split, so a single record serves both. `CustomerProfileDto` carries `Email`, which is **threaded in separately** because it lives on the Identity user, not the profile (`profile.ToDto(email)`). The mappers are **explicit extension methods, no AutoMapper** (per CODING_STANDARDS), and they order addresses **defaults-first** (`OrderByDescending(IsDefaultShipping).ThenByDescending(IsDefaultBilling).ThenBy(CreatedAt)`) so the UI gets a stable, useful order for free. The validators **mirror the EF column lengths** and enforce the ISO-3166 alpha-2 country regex `^[A-Za-z]{2}$` and a phone regex — so a bad value fails at the edge instead of at the database.

#### `Data/Migrations/20260614215709_0003_customer_profile.cs`

Auto-generated; creates the `CustomerProfile` and `Address` tables, the cascade FKs, and — the part to verify by eye — the two filtered unique indexes with their exact SQL predicates (`filter: "[IsDefaultShipping] = 1"` / `"[IsDefaultBilling] = 1"`) and `Country` as `nchar(2)` fixed-length. This confirms the configuration actually reached the schema; never hand-edit it.

### Frontend — `src/web/src/features/account/`

#### `lib/account-schema.ts`

The keystone of the frontend slice. The **zod schemas mirror the backend FluentValidation rules** field-for-field (same lengths, same phone regex, same `^[A-Za-z]{2}$` country rule), so a bad value fails inline instead of round-tripping to a 422. It also exports **form→API mappers** (`toUpsertProfileBody`, `toAddressBody`) that convert the form's empty string into the API's `null` and upper-case the country — the same normalization the server does, repeated client-side so the optimistic UI stays consistent. Critically, the body types are `components['schemas']['...']` from the generated OpenAPI schema, so a backend contract change is a **TypeScript compile error**.

#### `hooks/useProfileQuery.ts` + `hooks/useAccountMutations.ts`

One query key, `['profile']`, holds the profile *and* its addresses. **Every mutation** (`useUpdateProfile`, `useAddAddress`, `useUpdateAddress`, `useDeleteAddress`) invalidates that single key on success — so one refetch keeps the whole page in sync, and there is no manual cache surgery. Because lazy creation lives server-side, `useProfileQuery` "never 404s for a real customer" — there is **no empty/onboarding state to build**. The CSRF header rides along automatically via the client middleware from Story 0.3.

#### `components/ProfileForm.tsx`, `AddressForm.tsx`, `AddressSection.tsx`

`ProfileForm` and `AddressForm` are **React Hook Form + `zodResolver`** — RHF owns the field state, zod owns validation, and the two checkboxes map straight to `IsDefaultShipping`/`IsDefaultBilling`. **Email is rendered as a disabled `<Input>`**, not a form field, because it's the immutable login identity. `AddressSection` is the orchestrator: a single `editing` state is the address id being edited, the sentinel `'new'` for the add form, or `null` when just listing — one piece of state driving the whole add/edit/delete UX, with default badges and a `window.confirm` delete guard.

### Story 1.4 — what to know cold

1. **Identity user ≠ domain profile.** `ApplicationUser` owns authentication; `CustomerProfile` owns domain data. The FK between them is a **`string`** because Identity's PK is a GUID stored as `nvarchar(450)` — type-mismatch it and the relationship won't build.
2. **Lazy creation is made race-safe by the unique index + catch-and-re-read.** Catch `DbUpdateException`, detach the loser, re-read the winner, return it; rethrow if no winner appeared. This turns a TOCTOU race into an idempotent no-op instead of a 500.
3. **"One default per axis" is guaranteed twice:** a SQL Server *filtered unique index* (`[IsDefaultShipping] = 1`) is the hard constraint, and the service's *clear-then-set inside a transaction* is what stops a legitimate change from tripping it. The `ExecuteUpdate` clear hits the DB before the staged insert flushes — that ordering is the subtlety.
4. **`ExecuteUpdate` bypasses the `AuditingInterceptor`,** so set-based clears must stamp `UpdatedAt`/`UpdatedBy` by hand or lose their audit trail.
5. **Not-yours returns 404, never 403,** and the user id comes from the auth cookie, never the route/body — together these close an enumeration oracle and make cross-user access structurally impossible.
6. **PII never enters a log or exception message** — only the user id and entity ids do — because exception messages reach clients and (in Dev) the stack trace.

---

## 6. Perf baseline + the security hardening pass

This section covers two things you did at the close of Phase 1: you measured the storefront read path with a real load tool (k6), and you ran a full adversarial security review over everything Phases 0–1 produced and fixed every finding. Both are the kind of work that *sounds* like a resume line on its own — but only if you can explain the numbers and the threat model. That's what this section makes you able to do.

### What is in `tests/load/` and `docs/perf/`

```
tests/load/
└─ catalog-browse.js              ← k6 script: anonymous shopper hits categories → grid → detail

docs/perf/
└─ baseline-2026-06-15.md         ← the recorded run: scenario, env, SLOs, results, honest caveats
```

The hardening pass touched files across the whole tree rather than a single folder — grouped by theme below, the notable ones are:

```
src/api/Retail.Api/
├─ Program.cs                                 ← +security headers, +HSTS, +Csrf:Key validation, +CORS warning
├─ Common/Helpers/ProductImage.cs             ← +TryDetectContentType (magic-byte sniff)
├─ Controllers/CatalogController.cs           ← +RequestSizeLimit, +sniff-then-store-detected-type
├─ Services/CatalogService.cs                 ← +delete-superseded-blob, +search min-length guard
├─ Services/AuthService.cs                    ← +login timing equalizer (dummy PBKDF2)
├─ Identity/AuthCookies.cs                    ← refresh cookie scoped to /api/v1/auth
├─ Identity/CsrfOptions.cs                    ← dedicated Csrf:Key (key separation)
├─ Storage/BlobStorageClient.cs               ← Lazy<BlobServiceClient>, private-by-default access
├─ Storage/BlobStorageOptions.cs             ← +PublicReadImages toggle (default false)
└─ Repositories/CustomerProfileRepository.cs  ← audit-stamp the ExecuteUpdate default-clear

src/web/src/
├─ lib/api/client.ts                          ← CSRF fail-fast, correct `csrf` cookie name
├─ app/router.tsx                             ← /admin scoped to Administrator only
└─ components/layouts/StorefrontShell.tsx     ← Admin link scoped to Administrator only

.github/
├─ workflows/ci.yml                           ← permissions: contents:read, SHA-pinned actions, Node 22
├─ workflows/iac.yml                          ← SHA-pinned actions
└─ dependabot.yml                             ← weekly github-actions bumps (keeps pins fresh)
```

### Per-file purpose

#### PART A — the k6 perf baseline

#### `tests/load/catalog-browse.js`

This is your **first load test** (PLAN.md §13 Phase 1 deliverable). It models one anonymous shopper repeating the storefront's entire public read surface — the three endpoints a storefront actually hammers — and nothing else:

1. `GET /api/v1/catalog/categories` — the filter panel
2. `GET /api/v1/catalog/products?Page=1&PageSize=12` — the product grid (half the iterations append a random `&CategoryId=` to exercise the **indexed filtered path**, not just the unfiltered scan)
3. `GET /api/v1/catalog/products/{slug}` — a product detail page

Two design choices are worth being able to defend. First, a **`setup()` discovery step**: before the test runs, it fetches the live product list and category list and hands the real slugs + category ids to every VU via the returned `data` object. That means the script is **not pinned to a specific seed set** — reseed the catalog and it still works; it even `throw`s a clear "seed the catalog before load testing" error if it finds zero products. Second, the **`sleep()` think-time** between steps (0.5s, 1s, 1s) makes the traffic shaped like a human browsing, not a tight benchmark loop — which is exactly why throughput here is think-time-bound (see the caveat below).

The load profile is a classic **VU ramp**: `0→20 VUs over 30s, hold at 20 for 1m, ramp back to 0 over 30s` (~2 min total) — a modest concurrency baseline, not a stress test.

**The resume-gold part is the `thresholds` block — these are SLOs as code:**

```js
thresholds: {
  http_req_failed: ['rate<0.01'],     // <1% transport/5xx failures
  http_req_duration: ['p(95)<500'],   // 95th percentile under 500ms
  checks: ['rate>0.99'],              // >99% of assertions pass
  browse_errors: ['rate<0.01'],
}
```

When a threshold is breached, **k6 exits non-zero** — so the nightly `load-test.yml` job (Phase 10/11) turns a latency regression into a red build, exactly like a failing unit test. That's the difference between "I ran a load test once" and "I gated latency in CI." Note the custom `browse_errors` `Rate` metric: a request can return HTTP 200 but have a *wrong body* (e.g. `items` isn't an array). A failed `check()` feeds `browse_errors`, so a **silently malformed-but-200** response still counts against the error budget — `http_req_failed` alone would miss it.

#### `docs/perf/baseline-2026-06-15.md`

The recorded run. The headline result: **all thresholds passed**, p95 of **5.67ms** (≈88× under the 500ms budget), **0.00% failed** requests (0/2,198), **100%** checks passed, across 732 iterations at 20 peak VUs.

What makes this doc interview-grade is not the green numbers — it's the **honest caveats** that frame them. You must be able to recite these, because a sharp interviewer will ask "is 5ms real?" and the right answer is "no, and here's why":

1. **Tiny dataset — 3 products, 1 category.** Result sets are trivially small and SQL Server caches the whole thing, so absolute latencies are *optimistic*. A representative baseline needs hundreds of products across several categories to actually exercise paging and filtered scans.
2. **Debug build + loopback.** It ran a `Debug` build via `dotnet run` on `localhost` — no `Release` optimizations, no real network, no APIM/Container Apps hops. The production-comparable number is the nightly run against *staging*.
3. **Latency-only, not a saturation test.** With ~2.5s of think-time per iteration, 20 VUs can only offer ~8 iterations/s — so ~18 req/s is **think-time-bound, not server-bound**. This measures latency under light concurrency, *not* the max-throughput breaking point. The follow-up is an arrival-rate (no think-time) stress run.

**Interview gotcha:** the value of a baseline isn't the absolute number — it's that it's *recorded and reproducible*, so the Phase 10 optimization work has a before/after to point at. A pretty number with no documented environment is worthless for regression detection.

#### PART B — the security/best-practices hardening pass

The framing matters: you ran a **full adversarial review** over Phases 0–1 and it found **0 high / 0 medium** severity issues. The entire set of remaining **low/info** findings was then fixed. "Zero high/zero medium on first review" is a stronger signal than "found and fixed a critical bug" — it says the architecture was sound and the cleanup was hygiene, not firefighting. Below, each fix is grouped by theme with its *why it matters*.

##### Auth & session hardening — `AuthService.cs`, `AuthCookies.cs`, `CsrfOptions.cs`

The **login timing equalizer** (resume-gold) is the subtlest fix. When you look up a user by email and they don't exist, the naive code returns immediately — but a real password check runs an expensive PBKDF2 hash. That latency *difference* is an oracle: an attacker measures response time and learns which emails are registered (**account enumeration**). The fix verifies the supplied password against a precomputed dummy hash on the unknown-email branch:

```csharp
private static readonly string DummyPasswordHash =
    new PasswordHasher<ApplicationUser>().HashPassword(new ApplicationUser(), "timing-equalizer");
// ...on the user-is-null branch:
_ = _userManager.PasswordHasher.VerifyHashedPassword(new ApplicationUser(), DummyPasswordHash, request.Password);
```

Now "no such account" costs the same PBKDF2 as "wrong password," and both return the identical `InvalidCredentials` message — login is **non-enumerable** by both content *and* timing. (The code also honestly documents the deliberate counterpart: `/register` *does* disclose "email already exists" for UX, an accepted tradeoff deferred until a transactional-email pipeline exists.)

The **dedicated `Csrf:Key`** is key separation: the CSRF HMAC was previously able to reuse `Jwt:Key`. Now `CsrfOptions` binds a separate ≥32-char secret, validated `OnStart` in `Program.cs` (fail-fast if missing). **Why two keys for two HMACs?** So a leak or rotation of the JWT signing key never forces CSRF rotation and vice versa — the two cryptographic purposes (a token the server signs for *itself* vs. one the client echoes back) stay independent.

The **refresh-cookie path scoping** in `AuthCookies.cs` is least-privilege for cookies. Only `/auth/refresh` and `/auth/logout` ever read the refresh token, so it's scoped to `Path = "/api/v1/auth"` instead of being sent on every single API request like the access/CSRF cookies. **Interview gotcha:** the matching `Clear()` *must* delete it with the **same path** — a cookie deleted at `/` does not match a cookie set at `/api/v1/auth`, and the browser silently keeps the original. Getting delete-path wrong is a real "logout didn't actually log me out" bug.

##### HTTP hardening — `Program.cs`

Right after `ExceptionMiddleware` (so even error responses carry them), a small middleware stamps **baseline security response headers**: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, and a strict `Content-Security-Policy: default-src 'none'; frame-ancestors 'none'` — appropriate because this is a pure JSON API that renders no HTML of its own. **HSTS** (`UseHsts()`) is emitted **outside Development only**.

**Interview gotcha:** there is deliberately *no* `UseHttpsRedirection`. TLS is terminated at the APIM/Container Apps edge, so the app sees plaintext HTTP behind the proxy — a redirect there would be a no-op. But `Strict-Transport-Security` still has merit: it tells the *browser* to refuse plaintext on future visits. You kept the half of the HTTPS story that works behind a proxy and dropped the half that doesn't. There's also a startup `Log.Warning` when `Cors:AllowedOrigins` is empty — fail-closed, but loud, to shorten "why is CORS blocking me" debugging.

##### Uploads & storage — `ProductImage.cs`, `CatalogController.cs`, `BlobStorageClient.cs`, `CatalogService.cs`

The **magic-byte sniff** is the centerpiece. The client-supplied `Content-Type` is trivially spoofable — anyone can `POST` an executable with `Content-Type: image/png`. `ProductImage.TryDetectContentType` reads the leading bytes and matches real file signatures (`FF D8 FF` = JPEG, `89 50 4E 47…` = PNG, `RIFF…WEBP` = WebP):

```csharp
public static bool TryDetectContentType(ReadOnlySpan<byte> header, out string contentType)
```

The controller does a cheap early reject on the declared type, then makes the **sniff authoritative**: it reads 12 header bytes via `ReadAtLeastAsync`, rejects if no signature matches, **rewinds the stream**, and stores the blob with the *detected* type, never the client's claim. Paired with this are **request-size limits at the framework edge** — `[RequestSizeLimit(ProductImage.MaxBytes)]` + `[RequestFormLimits(MultipartBodyLengthLimit = …)]` — which reject an oversized body *before* model binding buffers it, so the 5 MB cap isn't only a post-buffer check (a 2 GB upload no longer gets fully read into memory first).

In `BlobStorageClient.cs`, two things: the container is now **private by default** — `PublicReadImages` (default `false`) gates `PublicAccessType.Blob` vs `.None`, so production isn't anonymously readable unless explicitly opted in (dev/Azurite sets it `true`). And the `BlobServiceClient` is built through a **`Lazy<BlobServiceClient>`** rather than in the constructor:

```csharp
_serviceClient = new Lazy<BlobServiceClient>(
    () => new BlobServiceClient(_options.ConnectionString, ClientOptions));
```

**Why Lazy?** This is a singleton; building the client in the constructor would touch the connection string the moment the service is resolved — so a blank/invalid connection string would fail *every unrelated catalogue read*, not just an actual blob op. `Lazy` defers that to first real use while still **reusing one client and its pooled HTTP pipeline** (per Azure SDK guidance — constructing a `BlobServiceClient` per request leaks sockets). Finally, `CatalogService` now **deletes the superseded blob** on image replace — but *after* the DB pointer is committed and wrapped in try/catch, so a cleanup failure never leaves a dangling DB reference or fails the user's request.

##### Audit correctness — `CustomerProfileRepository.cs`

When you set a new default shipping address, a set-based `ExecuteUpdateAsync` clears the old default in one SQL `UPDATE`. **Interview gotcha:** `ExecuteUpdate`/`ExecuteDelete` bypass the EF change tracker entirely — which means they **skip the `AuditingInterceptor`**. So those rows would silently keep stale `UpdatedAt`/`UpdatedBy`. The fix threads `updatedAt` + `updatedBy` in as parameters and stamps them explicitly inside the `SetProperty` chain. The lesson is general: the interceptor only fires on tracked `SaveChanges`; any bulk-SQL path needs to stamp audit fields by hand.

##### CI / supply-chain hardening — `ci.yml`, `iac.yml`, `dependabot.yml`

Three changes. A top-level **`permissions: contents: read`** caps `GITHUB_TOKEN` to read-only regardless of the repo/org default — least privilege for a build/test workflow that runs on fork PRs and holds no deploy secrets. Every action is **SHA-pinned** (`actions/checkout@df4cb1c…d31b… # v6`) instead of a mutable `@v6` tag — pinning to a full commit SHA closes the **mutable-tag supply-chain vector** (a compromised maintainer can re-point `v6` at malicious code; they can't change a commit SHA). To keep pins from going stale, **`dependabot.yml`** opens weekly `github-actions` PRs that bump each SHA *and* its trailing version comment. The web toolchain also moved off **EOL Node 20 → Node 22**.

##### Frontend — `client.ts`, `router.tsx`, `StorefrontShell.tsx`

The **CSRF fail-fast**: when a state-changing request finds no `csrf` cookie, the middleware now throws a diagnosable client error instead of firing a request that's guaranteed to come back 403 — the UI maps it to "refresh the page and try again." This also fixed a stale comment/name: the cookie is `csrf` echoed as `X-CSRF-Token`, not the old `XSRF-TOKEN`. Separately, the `/admin` route and the header Admin link were **scoped to `Administrator` only** (dropping `StoreManager`), matching the Administrator-only backend admin endpoints — removing a dead-end where a StoreManager could see the link but got bounced to `/login`. **The principle:** frontend authz is UX, never security — but it should still *mirror* the backend's real authorization, or you ship confusing dead-ends. StoreManager gets backed admin routes when the Phase 3 RBAC matrix lands.

### Perf + hardening — what to know cold

1. **k6 thresholds are SLOs-as-code.** A breached `p(95)<500`, `http_req_failed<1%`, or `checks>99%` makes k6 exit non-zero, so the nightly load job turns a latency regression into a red build — the same gate as a failing unit test.
2. **A baseline is only worth its caveats.** The 5.67ms p95 is ~88× under budget *because* of a 3-product dataset, a Debug build, and loopback — the number is a reproducible relative floor for regression detection, not a production SLA.
3. **The client Content-Type is a lie; magic bytes are the truth.** `ProductImage.TryDetectContentType` sniffs real file signatures and the controller stores the *detected* type — and `RequestSizeLimit` rejects oversized bodies at the framework edge before buffering.
4. **Login must be non-enumerable by timing, not just by message.** Burning an equivalent PBKDF2 on the unknown-email branch closes the timing oracle; `/register`'s enumeration is a documented, deliberate UX tradeoff.
5. **Cookie security lives in one auditable place, and delete-path must match write-path.** The refresh cookie is scoped to `/api/v1/auth` (least privilege) and `Clear()` deletes it with the identical path or the browser keeps it.
6. **`ExecuteUpdate`/`ExecuteDelete` bypass the `AuditingInterceptor`** — any bulk-SQL write must stamp `UpdatedAt`/`UpdatedBy` by hand.
7. **SHA-pin actions + least-privilege `GITHUB_TOKEN` + Dependabot.** Pinning to a commit SHA (not a mutable `@v6` tag) closes the supply-chain vector; `permissions: contents: read` caps the token; Dependabot keeps the pins fresh.

---

## 7. File relationship maps

These trace the real Phase 1 flows end to end. Read each top to bottom; the inline notes are the load-bearing "why this call, here". File names, methods, routes, and cookie/header names are exactly as they appear in the code.

### Login → cookies issued

```
SPA LoginPage submits → apiClient.POST('/api/v1/auth/login', { body })
   │  (csrfMiddleware skips it: login is the FIRST request, no session yet —
   │   but the SPA already seeded the csrf cookie via GET /auth/csrf on load)
   ▼
AuthController.Login([FromBody] LoginRequest)         [Route("api/v1/auth")] [AllowAnonymous]
   │
   ├─ _loginValidator.ValidateAsync(...)  → 422 ApiResponse.Fail on invalid shape
   │
   ▼
AuthService.LoginAsync(request, ct)
   │
   ├─ _userManager.FindByEmailAsync(email)
   │     └─ null? → VerifyHashedPassword against DummyPasswordHash   ← burns an equal PBKDF2
   │              → AuthResult.Fail(InvalidCredentials)               (timing-oracle defense)
   │
   ├─ _signInManager.CheckPasswordSignInAsync(user, pwd, lockoutOnFailure: true)
   │     ├─ IsLockedOut → AuthResult.Fail(LockedOut)        ← 5 fails / 15-min policy (Program.cs)
   │     └─ !Succeeded  → AuthResult.Fail(InvalidCredentials)
   │
   ▼
AuthService.IssueTokensAsync(user, ct)                  ← shared by login / register / refresh
   │
   ├─ _jwtService.CreateAccessToken(user, roles)
   │     └─ JwtService: sub=user.Id, jti=Guid, roles as ClaimTypes.Role, HMAC-SHA256
   │        returns (accessToken, accessExpiresAt = now + AccessTokenMinutes)
   │
   ├─ rawRefresh = SecureTokens.NewToken();  hash = SecureTokens.Sha256(rawRefresh)
   │     └─ ONLY the hash is persisted (new RefreshToken row); the raw value goes in the cookie
   │
   └─ _refreshTokens.AddAsync(newToken) → SaveChangesAsync()
   │
   ▼  AuthResult.Success(AuthTokens{ access, refresh(raw), user })
AuthController.HandleAuthResult(result)
   │
   ├─ AuthCookies.WriteAccessToken(...)   → Set-Cookie access_token   (HttpOnly, SameSite=Strict, Path=/)
   ├─ AuthCookies.WriteRefreshToken(...)  → Set-Cookie refresh_token  (HttpOnly, Path=/api/v1/auth ← least-privilege)
   ├─ AuthCookies.WriteCsrf(_csrf.Issue())→ Set-Cookie csrf           (NON-HttpOnly session cookie)
   └─ return Ok(ApiResponse<AuthUserDto>.Ok(user))   ← body carries the USER, never a token
```

### Authenticated request + refresh rotation

```
SPA fires any request → apiClient (credentials: 'include')
   │  browser auto-attaches the access_token cookie (JS can't read it — HttpOnly)
   ▼
JwtBearer middleware: OnMessageReceived (Program.cs)
   │  context.Request.Cookies.TryGetValue("access_token", out token) → context.Token = token
   │  (the JWT rides in the COOKIE, not the Authorization header; this hook bridges the two)
   ▼
JwtBearer validates token → builds HttpContext.User  (sub → NameIdentifier, roles → ClaimTypes.Role)
   │
   ▼
[Authorize] / [Authorize(Roles = ...)] on the action
   │
   ├─ token valid   → action runs
   └─ expired/missing → 401  ──────────────────────────────────┐
                                                               │
   ┌───────────────────────────────────────────────────────────┘
   ▼  SPA sees 401 on a protected call → POSTs /api/v1/auth/refresh
AuthController.Refresh(ct)
   │  refreshToken = Request.Cookies["refresh_token"]   (only this path receives it)
   ▼
AuthService.RefreshAsync(refreshToken)
   │  hash = Sha256(refreshToken) → _refreshTokens.GetByHashAsync(hash)
   │
   ├─ stored == null            → Fail(InvalidRefreshToken)
   ├─ stored.RevokedAt != null  → REUSE DETECTED:                         ← a rotated-away token
   │      ListNotRevokedByUserAsync(userId) → revoke ALL live tokens          replayed = stolen
   │      (ReasonRevoked="reuse-detected") → SaveChanges → Fail            → global logout of the thief
   ├─ stored.ExpiresAt <= now   → revoke "expired" → Fail
   │
   └─ valid + active → IssueTokensAsync(user, replacing: stored)
   │      └─ mints successor; stored.RevokedAt=now, ReasonRevoked="rotated",
   │         ReplacedByTokenHash=newHash   ← one-time-use rotation chain
   ▼
HandleAuthResult: re-writes access_token + refresh_token + csrf cookies
   │   (on ANY failure → AuthCookies.Clear(...) so the SPA falls back to /login)
```

### CSRF double-submit on a state-changing request

```
SPA mutation (e.g. useUpdateAddress) → apiClient.PUT('/api/v1/profile/addresses/{id}', ...)
   │
   ▼
csrfMiddleware.onRequest  (lib/api/client.ts)   ← STATE_CHANGING_METHODS = POST/PUT/PATCH/DELETE
   │  token = readCookie('csrf')                  (readable because the cookie is NON-HttpOnly)
   │  token == null? → throw 'Missing CSRF token' (fail fast, no guaranteed-403 round-trip)
   │  request.headers.set('X-CSRF-Token', token)
   ▼
fetch sends:  cookie access_token (HttpOnly) + cookie csrf + header X-CSRF-Token  (same value, twice)
   ▼
Pipeline order (Program.cs):  UseAuthentication → UseAuthorization → CsrfMiddleware → MapControllers
   ▼
CsrfMiddleware.InvokeAsync(context, ICsrfTokenService csrf)   ← csrf injected per-request
   │  method in SafeMethods (GET/HEAD/OPTIONS/TRACE)? → pass through untouched
   │
   │  cookie = Request.Cookies["csrf"];  header = Request.Headers["X-CSRF-Token"]
   │  valid =  !empty(cookie) && !empty(header)
   │        && SecureTokens.FixedTimeEquals(cookie, header)   ← constant-time, no early-exit leak
   │        && csrf.Validate(cookie)                          ← HMAC signature check (signed double-submit)
   │
   ├─ valid    → await _next(context)  → controller action runs
   └─ !valid   → 403 ApiResponse.Fail("CSRF validation failed", code=CSRF_VALIDATION_FAILED)
```

`csrf.Validate` re-signs the `{random}` half with the server-only `Csrf:Key` and compares it to the `{signature}` half — so an attacker who plants a `csrf` cookie can't forge a value that passes, even though they could echo it back as the header.

### Storefront catalog read

```
CatalogPage → useProductsQuery({ page, pageSize, categoryId, search })   (TanStack Query)
   │  queryKey ['products', params] → queryFn
   ▼
apiClient.GET('/api/v1/catalog/products', { params: { query: { Page, PageSize, CategoryId, Search } } })
   │  (GET → csrfMiddleware adds NO header; query params are PascalCase ← ASP.NET binds by property name)
   ▼
CatalogController.ListProducts([FromQuery] ProductListQuery)   [AllowAnonymous]
   ▼
CatalogService.ListProductsAsync(query)
   │  page = Max(1, query.Page);  pageSize = Clamp(query.PageSize, 1, 100)   ← caps abuse
   │  search = NormalizeSearch(...)  ← drops <2-char terms (non-sargable LIKE on a public endpoint)
   ▼
ProductRepository.ListPublishedAsync(categoryId, search, page, pageSize)
   │  _db.Products.AsNoTracking().Where(p => p.IsPublished)         ← read path, no change tracking
   │     + optional CategoryId / Name|Description LIKE filters
   │  ── RetailDbContext global query filter: HasQueryFilter(p => !p.IsDeleted) ──
   │     soft-deleted rows are excluded by EF on EVERY query, invisibly
   │  CountAsync(total) → OrderBy(Name).Skip(...).Take(...).Include(Variants)
   ▼
CatalogService: items.Select(p => p.ToSummaryDto())  (CatalogMappers)
   │  → new PagedResult<ProductSummaryDto>(dtos, total, page, pageSize)   (TotalPages/HasNext computed)
   ▼
CatalogController: Ok(ApiResponse<PagedResult<ProductSummaryDto>>.Ok(result))
   ▼
queryFn returns data.data → cached under ['products', params] → ProductCard grid renders
```

### Admin image upload

```
Admin ProductForm → useUploadProductImage({ id, file })
   │  bodySerializer override builds a FormData (browser sets the multipart boundary itself)
   ▼
apiClient.POST('/api/v1/catalog/products/{id}/image', multipart)
   │  POST → csrfMiddleware attaches X-CSRF-Token  → CsrfMiddleware passes
   ▼
CatalogController.UploadProductImage(Guid id, IFormFile file)   [Authorize(Roles = Administrator)]
   │  [RequestSizeLimit(5MB)] / [RequestFormLimits] → oversized body rejected at the framework EDGE,
   │  before model binding buffers it
   │
   ├─ file null/empty / > MaxBytes              → 422
   ├─ !ProductImage.IsAllowedContentType(...)   → 422   (fast reject on the SPOOFABLE declared type)
   ├─ read first 12 bytes → ProductImage.TryDetectContentType(header, out detectedContentType)
   │     └─ magic-byte sniff: JPEG FF D8 FF / PNG 89 50 4E 47… / WebP "RIFF"…"WEBP"   ← AUTHORITATIVE
   │     └─ no match → 422   |   match → stream.Position = 0  (rewind before storing)
   ▼
CatalogService.SetProductPrimaryImageAsync(id, stream, detectedContentType)
   │  product = GetByIdForWriteAsync(id)   (404 → NotFoundException → ExceptionMiddleware)
   │  previousKey = product.PrimaryImageBlobKey
   │  blobKey = $"products/{id:N}/{Guid:N}.{ext}"   ← unique per upload, never overwrite
   ▼
BlobStorageClient.UploadAsync(_storage.ProductImagesContainer, blobKey, content, DETECTED type)
   │  Lazy<BlobServiceClient> built on first use (pinned ServiceVersion for Azurite compat)
   │  CreateIfNotExistsAsync → product-images container → blob stored with detected Content-Type
   ▼
CatalogService: product.PrimaryImageBlobKey = blobKey → SaveChangesAsync()   ← pointer committed FIRST
   │  then best-effort DeleteAsync(previousKey)  ← AFTER commit, failure only logged (no dangling ref)
   ▼
Ok(ApiResponse<ProductDetailDto>.Ok(product))  → SPA invalidates ['products'] + admin detail cache
```

### Customer address set-default

```
AccountPage → AddressForm (IsDefaultShipping/IsDefaultBilling) → useUpdateAddress({ id, body })
   │  PUT → csrfMiddleware attaches X-CSRF-Token
   ▼
CustomerProfileController.UpdateAddress(Guid id, [FromBody] AddressRequest)
   │  [Authorize(Roles = Customer)] (whole controller)
   │  userId = _currentUser.UserId   ← from the cookie/JWT, NEVER the route or body (can't address others)
   │  _addressValidator.ValidateAsync → 422 on invalid
   ▼
CustomerProfileService.UpdateAddressAsync(userId, addressId, request)
   │  address = _repo.GetOwnedAddressAsync(userId, addressId)
   │     └─ filtered by AppUserId → null if missing OR not yours → 404 (never confirms a foreign id)
   │  ApplyAddressFields(address, request)
   │
   ▼  await using tx = _db.Database.BeginTransactionAsync()      ← clear-then-set must be ATOMIC
   │
   ├─ ClearSupersededDefaultsAsync(profileId, exceptAddressId: addressId, request, userId)
   │     └─ if request.IsDefaultShipping → _repo.ClearDefaultShippingAsync(...)
   │           ExecuteUpdateAsync: set-based UPDATE, IsDefaultShipping=false on the OLD default
   │           ── bypasses AuditingInterceptor, so it stamps UpdatedAt/UpdatedBy by hand ──
   │     └─ (same for IsDefaultBilling)
   │
   ├─ SaveDefaultChangeAsync(tx):  SaveChangesAsync() → tx.CommitAsync()
   │     └─ AuditingInterceptor stamps the tracked address's UpdatedAt/UpdatedBy on this save
   │     └─ catch SqlException 2601/2627 (a concurrent same-axis default) → 409 ConflictException
   ▼
─ DB guarantee (AddressConfiguration) ─
   UX_Address_DefaultShipping / UX_Address_DefaultBilling : UNIQUE filtered indexes
   ("WHERE [IsDefaultShipping] = 1") → at most ONE default per axis per profile, enforced by SQL Server
   (clearing the prior default first is what keeps the index from tripping on a legitimate change)
   ▼
Ok(ApiResponse<AddressDto>.Ok(address)) → SPA invalidates ['profile'] → page refetches in sync
```

---

## 8. Patterns to remember

The Phase 1 additions to your interview toolkit, in rough priority order. (The Phase 0 patterns — the envelope, `ExceptionMiddleware`, the audit interceptor, the shadcn stack — still hold; these build on them.)

### 1. Cookie-JWT + refresh rotation + reuse detection (highest priority)

**The pattern:** short-lived access JWT in an **HttpOnly** cookie (XSS can't read it); a long-lived **opaque** refresh token stored only as a **SHA-256 hash**; every refresh **rotates** (issue successor, revoke predecessor, link them); presenting an already-revoked token = **reuse detection** → revoke the user's entire live token set.

**Why:** HttpOnly defeats token theft via XSS. Rotation shrinks the window a leaked refresh token is useful. Reuse detection turns a stolen-token replay into an automatic global logout — the thief and the victim both get kicked, and you find out.

**Resume claim:** "JWT-in-HttpOnly-cookie auth with single-use refresh-token rotation and reuse detection."

### 2. Signed double-submit CSRF

**The pattern:** because auth rides on cookies (auto-sent cross-site), every state-changing request must echo a `csrf` cookie value in an `X-CSRF-Token` header; the token is `{random}.{HMAC(serverKey, random)}` so it's **unforgeable without the key** and needs **no server-side state**.

**Interview gotcha:** why sign it? Naive double-submit breaks if an attacker can plant a cookie (sibling-subdomain XSS, HTTP MITM). The HMAC means a planted value won't validate. (And the CSRF key is now **separate from the JWT key** — key separation.)

### 3. The vertical slice: Controller → Service → Repository → DbContext

**The rule:** Controller does binding + validation + envelope only. Service owns business rules + transactions + throws domain exceptions. Repository is pure persistence. DbContext is EF.

**Why:** each layer is independently testable and has one reason to change. The controller never sees a `DbContext`; the repository never decides anything. **Interview gotcha:** the service is the *only* layer allowed to grab `RetailDbContext` directly — and only to open a transaction for a multi-table write.

### 4. Soft-delete global query filter + filtered unique indexes

**The pattern:** `builder.Entity<Product>().HasQueryFilter(p => !p.IsDeleted)` hides deleted rows from *every* query automatically; uniqueness is enforced with `WHERE IsDeleted = 0` so a deleted SKU can be reused.

**Interview gotcha:** to declare *multiple* indexes on the *same* column (the address default-shipping/billing case), you need the **named `HasIndex(expr, "Name")` overload** — EF collapses unnamed duplicates into one (last wins).

### 5. Concurrency-safe invariant: clear-then-set + DB-enforced + transactional

**The pattern (the "one default address per axis" invariant):** the rule is enforced **twice** — a filtered unique index makes the database refuse two defaults, *and* the service clears the prior default with a set-based `ExecuteUpdate` **before** setting the new one, **inside a transaction**, so the index never trips mid-write.

**Why:** defense in depth. App logic gives a clean UX; the DB index is the backstop that survives bugs and races. **Interview gotcha:** `ExecuteUpdate` bypasses the `SaveChanges`-based audit interceptor, so you stamp `UpdatedAt`/`UpdatedBy` yourself in the update.

### 6. Owner-scoping + 404-not-403 (IDOR defense)

**The pattern:** every owner-scoped lookup is constrained to the caller's id in the query itself (`WHERE a.CustomerProfile.AppUserId == callerId`); a miss throws `NotFoundException` → **404**, never 403.

**Why:** the user id comes from the auth cookie, never the route/body, so you can't address another user's data. Returning 404 (not 403) means you never even confirm that someone else's address id *exists*.

### 7. FluentValidation explicit → 422, mirroring the EF config

**The pattern:** validators are invoked explicitly in the controller (not via deprecated auto-validation) and return the envelope with `Field`-tagged errors at **422**; their length/required rules **mirror the EF `HasMaxLength`** so the client fails fast and the DB never sees junk.

### 8. TanStack Query for server state; mutations invalidate the cache

**The pattern:** reads are `useQuery` hooks keyed by their params; writes are `useMutation` that **invalidate** the relevant query keys on success, so the UI refetches the truth instead of guessing it. Zustand holds only the tiny auth identity (never server data).

### 9. React Hook Form + zod, mirroring the backend, converting at the boundary

**The pattern:** forms use RHF + a zod schema whose rules mirror the server validators (instant inline feedback, no round-trip per typo); the form→API mapper lives in one place and converts the human units (dollars, empty strings) to the API units (**integer cents**, nulls).

### 10. Typed `openapi-fetch` client — contract drift is a compile error

**The pattern:** `pnpm gen:api` regenerates `schema.d.ts` from the live Swagger; `apiClient.POST('/api/v1/...', …)` is fully typed. A backend contract change that the frontend hasn't caught up to becomes a **TypeScript build failure**, not a runtime 500.

**Interview gotcha:** ASP.NET documents **query** params by their PascalCase property name (`Page`, `PageSize`, `CategoryId`, `Search`) while request **bodies/responses** are camelCase — so the generated client mixes casing by location, and DTO fields come through optional (`priceCents?`) and must be guarded.

### 11. Secure file upload

**The pattern:** validate the **magic bytes**, not the spoofable `Content-Type`; cap the body with `[RequestSizeLimit]`/`[RequestFormLimits]` so it's rejected before buffering; store under a server-generated blob key; the container is **private by default** (a config toggle opts dev into public-read).

**Resume claim:** "Hardened image upload with content-sniffing and request-size limits; private-by-default blob storage."

### 12. PII never enters logs or exception messages; secrets never enter source

**The rule:** log only the safe identifiers (user id Guid, entity id, SKU, amount-in-cents) — never email/phone/address (or, in Phase 2, card data). Secrets (`Jwt:Key`, `Csrf:Key`, the admin password) live in user-secrets / Key Vault; `appsettings` carries only blank placeholders.

---

## 9. What's next

### Phase 2 — Cart & Orders

Phase 1 gave a shopper a catalog to browse and an account to own. Phase 2 lets them **buy**. It's the biggest story in the project (Epic 2 = four sibling stories):

| Story | What lands |
|---|---|
| **2.1 Cart** | Guest carts (an `X-Anon-Cart-Key` cookie) *and* authenticated carts, with **login-merge**; add/update/remove lines; a per-line price snapshot; the storefront cart drawer. |
| **2.2 Checkout** | **Stripe hosted Checkout** (test mode forever — the app never touches a card); a signature-verified, **idempotent** webhook (`ProcessedStripeEvent`) that creates the `Order` on `checkout.session.completed`. |
| **2.3 Inventory & concurrency** | Two-phase stock (reserve → commit on payment) with `InventoryItem.RowVersion` **optimistic concurrency** → 409, and a `CartExpirySweeper` background service. |
| **2.4 Order viewing** | The customer "My Orders" list / detail / cancel. |

Patterns Phase 2 will add to this recap: the **outbox/idempotency** pattern for webhooks, **optimistic concurrency** with rowversion, a **background service** with `IServiceScopeFactory`, and the order **snapshot** pattern (freeze price/address at purchase time).

### Open decision before Phase 2

The **checkout** chunk (not the cart chunk) needs a payment path. Three options, your call: **real Stripe test keys** + the Stripe CLI for live webhook testing; a **mocked Stripe client** with self-signed webhook integration tests (CI-provable, no third party); or a **no-Stripe stub** that simulates payment success. The cart slice is buildable and verifiable with none of them.

### Where to look up things later

- **"What did Phase 1 build?"** → this file
- **"What did Phase 0 build?"** → `phase0_recap.md` (same folder)
- **"What does this specific file do?"** → the heavy comment block at the top of every file
- **"Why did we choose X?"** → `docs/adr/<NNNN>-<topic>.md` (ADR-0007 = the cookie-JWT/CSRF decision)
- **"What's the current task / where are we?"** → memory's `project_progress.md`
- **"What's the locked stack?"** → memory's `tech_decisions.md` + `docs/PLAN.md`
- **"What's the data model?"** → `docs/DATABASE_DESIGN.md`
