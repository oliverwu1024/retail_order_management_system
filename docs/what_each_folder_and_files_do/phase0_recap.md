# Phase 0 Recap — What You Built and Why

> A self-learning recap of every concept, file, and connection introduced in
> Phase 0 (Stories 0.1–0.5). Read top to bottom the first time; later, use
> the table of contents to jump back to specific patterns.

## Table of contents

1. [The big picture — what does Phase 0 actually give you?](#1-the-big-picture)
2. [Story 0.2 — Backend skeleton (the meat)](#2-story-02--backend-skeleton)
3. [Story 0.3 — Frontend skeleton](#3-story-03--frontend-skeleton)
4. [Story 0.4 — Docker local dev](#4-story-04--docker-local-dev)
5. [Story 0.5 — CI/CD + Bicep IaC](#5-story-05--cicd--bicep-iac)
6. [File relationship maps](#6-file-relationship-maps)
7. [Patterns to remember (interview material)](#7-patterns-to-remember)
8. [What's next — Phase 1 preview](#8-whats-next)

---

## 1. The big picture

### What you built

Phase 0 produces a **bootable, observable, deployable skeleton** of a modern .NET 10 + React 19 web app — with zero business logic. Every architectural seam needed for Phases 1–10 is wired and tested, so feature work can flow without re-deciding cross-cutting concerns.

### Three layers, one envelope

```
┌──────────────────────────────────────────────────────────────────────┐
│  Browser (React SPA)                                                 │
│    ├─ React Router v7 (routes)                                       │
│    ├─ TanStack Query (server state)                                  │
│    ├─ Zustand (client state — auth)                                  │
│    └─ apiClient (openapi-fetch + CSRF middleware)                    │
└──────────────────────────────────┬───────────────────────────────────┘
                                   │   fetch('/api/...', { credentials: 'include' })
                                   │   cookie: JWT (HTTP-only), CSRF token (readable)
                                   ▼
┌──────────────────────────────────────────────────────────────────────┐
│  ASP.NET Core 10 API (Retail.Api)                                    │
│    ExceptionMiddleware (outermost — catches everything)              │
│    │                                                                 │
│    ├─ Serilog request logging                                        │
│    ├─ AuthN (JwtBearer) → AuthZ                                      │
│    ├─ MVC Controllers ─→ Services ─→ Repositories ─→ DbContext       │
│    │                                                                 │
│    └─ Every response wrapped as ApiResponse<T>                       │
└──────────────────────────────────┬───────────────────────────────────┘
                                   │
                                   ▼
                          ┌──────────────────┐
                          │ SQL Server 2022  │  (Identity tables + future domain entities)
                          └──────────────────┘
```

### The envelope contract — the single most important pattern in Phase 0

Every API response, success or failure, has the same JSON shape:

```jsonc
{
  "success": true,
  "data": { /* payload */ },
  "message": "Service is healthy",
  "errors": null,
  "traceId": "6ccd42fbe80a2eaf071fed0d4bf09b19",
  "timestamp": "2026-06-08T07:13:57Z"
}
```

This is `ApiResponse<T>` on the backend. The frontend's `apiClient` parses it once; every controller produces it; `ExceptionMiddleware` produces it for failures too. **No matter what happens — a 200, a 404, a 500, a validation failure, an EF concurrency conflict — the client sees the same shape.**

The `traceId` field is the W3C trace ID from OpenTelemetry's current `Activity`. When a customer reports "checkout failed" with the traceId, you can paste it into Grafana Tempo / Jaeger and see the exact distributed trace. **This is your observability bullet.**

### Why these choices matter for the resume

Every file you wrote in Phase 0 maps to a defensible interview talking point. The mapping:

| Resume claim | The Phase 0 evidence |
|---|---|
| "ASP.NET 10 + EF Core 10, three-tier" | `Retail.Api/Controllers/`, `Services/`, `Repositories/`, `Data/RetailDbContext.cs` |
| "Identity + JWT in HTTP-only cookies" | `Program.cs` JWT bearer wiring + planned cookie wrapping in Phase 1 |
| "Distributed tracing with OpenTelemetry" | `Program.cs` `AddOpenTelemetry()` + `ApiResponse.TraceId` populated from `Activity.Current` |
| "Optimistic concurrency, audit interceptors" | `Data/Interceptors/AuditingInterceptor.cs` (resume-gold) |
| "Tailwind + shadcn/ui + 12+ component library" | `src/web/src/components/ui/*` (4 of 12 written in Phase 0) |
| "45% code duplication reduction" | `docs/perf/jscpd-baseline.md` is the contract for the Phase 10 comparison |
| "Bicep IaC for Azure Container Apps + APIM" | `infra/bicep/main.bicep` + 12 modules |
| "GitHub Actions CI with OIDC federation" | `.github/workflows/ci.yml` + `iac.yml` with `id-token: write` |

---

## 2. Story 0.2 — Backend skeleton

### What's in `src/api/Retail.Api/`

```
Retail.Api/
├─ Program.cs                              ← composition root (DI, middleware pipeline)
├─ Domain/
│  ├─ Entities/ApplicationUser.cs          ← Identity user with FirstName/LastName
│  └─ Common/IAuditableEntity.cs           ← marker interface for audit-tracked entities
├─ Data/
│  ├─ RetailDbContext.cs                   ← EF Core DbContext (IdentityDbContext<User>)
│  ├─ Interceptors/AuditingInterceptor.cs  ← auto-stamps audit fields on SaveChanges
│  └─ Migrations/                          ← 0000_init.cs creates 7 Identity tables
├─ Controllers/HealthController.cs         ← GET /api/health returns ApiResponse envelope
├─ Common/Models/ApiResponse.cs            ← envelope type (T, non-generic, ApiError)
└─ Middlewares/ExceptionMiddleware.cs      ← global error handler → ApiResponse envelope
```

### Per-file purpose

#### `Domain/Entities/ApplicationUser.cs`

Extends ASP.NET Identity's `IdentityUser`. Adds `FirstName` + `LastName`. Persisted to the `AspNetUsers` table by Identity.

**Why a class instead of using `IdentityUser` directly?** So you can add domain-specific fields without touching the framework. Standard convention.

#### `Domain/Common/IAuditableEntity.cs`

Marker interface for entities with audit fields:
```csharp
DateTimeOffset CreatedAt;
string? CreatedBy;       // user id (string GUID from Identity)
DateTimeOffset? UpdatedAt;
string? UpdatedBy;
```

**Why an interface, not an abstract class?** Because C# is single-inheritance — `ApplicationUser` already inherits from `IdentityUser`. An interface composes; a base class would force a choice between Identity behaviors and audit behaviors.

#### `Data/RetailDbContext.cs`

EF Core DbContext. Inherits `IdentityDbContext<ApplicationUser>`, which auto-registers the 7 Identity tables (`AspNetUsers`, `AspNetRoles`, etc.).

Two key lines in `OnModelCreating`:
```csharp
base.OnModelCreating(builder);                                // 1. Identity tables
builder.ApplyConfigurationsFromAssembly(typeof(RetailDbContext).Assembly);  // 2. domain configs
```

Line 1 is non-negotiable — skipping it leaves Identity half-configured. Line 2 is the convention you adopted: every entity gets a `Foo Configuration : IEntityTypeConfiguration<Foo>` class in `Data/Configurations/`, picked up automatically.

#### `Data/Interceptors/AuditingInterceptor.cs` (resume-gold)

EF Core `SaveChangesInterceptor`. Overrides **both** `SavingChanges` (sync) and `SavingChangesAsync` (async) and routes to a common `Stamp()` helper.

`Stamp()` walks the ChangeTracker:
- `EntityState.Added` → set `CreatedAt`, `CreatedBy`
- `EntityState.Modified` → set `UpdatedAt`, `UpdatedBy`, then `entry.Property(nameof(CreatedAt)).IsModified = false` (defensive — protects immutable created fields from accidental overwrite)

**Why an interceptor instead of setting fields in every service?** DRY across 30+ entities + catches all SaveChanges paths (controllers, hosted services, migrations, seeders) + single source of truth for the clock and the "current user" resolver.

**Why is it scoped, not singleton?** It depends on `IHttpContextAccessor` (request-scoped). A singleton would capture stale request state.

#### `Common/Models/ApiResponse.cs`

Three types:
1. **`ApiResponse<T>`** — generic envelope for endpoints that return data
2. **`ApiResponse`** (non-generic) — for endpoints with no payload (DELETE, etc.)
3. **`ApiError`** — one structured error: `Code` (machine-readable), `Message` (human), `Field` (validation path, nullable)

Each has `init`-only properties (immutable after construction) and static factory methods `Ok(...)` and `Fail(...)`. The `TraceId` defaults to `Activity.Current?.TraceId.ToString() ?? ""` — populated automatically by OpenTelemetry's HTTP middleware.

#### `Middlewares/ExceptionMiddleware.cs`

Conventional middleware (constructor takes `RequestDelegate`). Wraps every downstream layer in try/catch.

Maps exceptions to (status, errorCode, message) via switch expression:
- `DbUpdateConcurrencyException` → 409 `CONCURRENCY_CONFLICT`
- `UnauthorizedAccessException` → 403 `FORBIDDEN`
- `KeyNotFoundException` → 404 `NOT_FOUND`
- everything else → 500 `INTERNAL_ERROR`

Differs Development vs Production: Dev includes the full stack trace as an extra `ApiError`; Prod hides it (stack traces leak SQL fragments, file paths, library versions — CVE bait).

Two guard rails:
- `_env.IsDevelopment()` check for stack trace exposure
- `context.Response.HasStarted` check — if downstream middleware already wrote response bytes, you can't rewrite the status code; we log a warning and bail rather than throwing.

#### `Program.cs` — the composition root

The single longest file in the project. Read it as 11 numbered sections:

1. **Serilog bootstrap logger** — catches errors during builder setup
2. **Full Serilog config** — reads from appsettings, enriches with `FromLogContext`
3. **HttpContextAccessor + TimeProvider** — DI plumbing for interceptors and tests
4. **EF Core DbContext + AuditingInterceptor** registered together
5. **ASP.NET Identity** — `AddIdentity<ApplicationUser, IdentityRole>` + EF stores
6. **JWT Bearer auth** — `TokenValidationParameters` with ALL FOUR validations ON (issuer, audience, lifetime, signing key) — disabling any is the classic CVE
7. **MVC Controllers** + camelCase JSON
8. **FluentValidation** — `AddValidatorsFromAssemblyContaining<Program>`
9. **Swagger** with JWT bearer scheme so devs can paste tokens
10. **OpenTelemetry** — traces (AspNetCore + EF Core instrumentation) + metrics; Console exporter for dev, will swap to OTLP → Azure Monitor in prod
11. **Health checks** with `AddDbContextCheck` tagged `ready`

Then the **middleware pipeline order** (load-bearing):
```
ExceptionMiddleware (outermost — wraps everything)
  ↓
UseSerilogRequestLogging (one structured log line per request)
  ↓
UseSwagger / UseSwaggerUI (dev only)
  ↓
UseRouting
  ↓
UseAuthentication → UseAuthorization
  ↓
MapControllers + MapHealthChecks
```

#### `Controllers/HealthController.cs`

`[ApiController] [Route("api/[controller]")] [AllowAnonymous]`. Returns `ApiResponse<HealthPayload>`.

**Why this exists when MapHealthChecks already gives you `/health/live` + `/health/ready`?** Those return the built-in ASP.NET health-check JSON shape. The controller returns the project's standard `ApiResponse` envelope — so hitting `/api/health` smoke-tests the **full pipeline**: MVC + JSON serialization + envelope + OTel trace ID injection.

#### `Data/Migrations/20260608071305_0000_init.cs`

Auto-generated by `dotnet ef migrations add 0000_init`. Creates **7 Identity tables** + **7 indexes** (normalized email/username, foreign-key indexes on join tables).

You should NOT edit migration files by hand. If the schema is wrong, fix the entity / configuration class and add a NEW migration.

### Story 0.2 — what to know cold

1. **The envelope shape is the contract.** Every response — success or failure — has `{success, data, message, errors, traceId, timestamp}`.
2. **ExceptionMiddleware is the OUTERMOST middleware.** Its try wraps every downstream layer, including auth and routing.
3. **`base.OnModelCreating(builder)` must come first** in `OnModelCreating` when you inherit `IdentityDbContext` — otherwise Identity tables are silently incomplete.
4. **`SaveChangesInterceptor` overrides both sync AND async** SavingChanges — forgetting one means half your paths skip audit.
5. **JWT bearer needs all four validations ON.** Disabling `ValidateLifetime` or `ValidateIssuerSigningKey` is a CVE classic.

---

## 3. Story 0.3 — Frontend skeleton

### What's in `src/web/`

```
src/web/
├─ package.json                     ← scripts, deps
├─ vite.config.ts                   ← Vite + path alias + dev proxy
├─ tailwind.config.ts               ← Tailwind theme tokens
├─ postcss.config.js                ← Tailwind 3 is a PostCSS plugin
├─ tsconfig.app.json                ← TS config + paths alias
├─ eslint.config.js                 ← flat ESLint config with Prettier off-rules
├─ .prettierrc.json                 ← Prettier rules
├─ .jscpd.json                      ← duplication scanner config
├─ index.html                       ← Vite entry
└─ src/
   ├─ main.tsx                      ← root render (Providers + Router + Toaster)
   ├─ index.css                     ← Tailwind directives + design tokens
   ├─ app/
   │  ├─ providers.tsx              ← QueryClientProvider + devtools
   │  ├─ router.tsx                 ← createBrowserRouter routes
   │  └─ guards/RoleGuard.tsx       ← redirect non-authorized users
   ├─ components/
   │  └─ ui/
   │     ├─ button.tsx              ← shadcn-style Button (Slot + cva)
   │     ├─ input.tsx               ← bare Input with shadcn styles
   │     ├─ card.tsx                ← compound: Card/Header/Title/Content/Footer
   │     ├─ toast.tsx               ← Radix Toast primitives wrappers
   │     └─ toaster.tsx             ← singleton renderer
   ├─ hooks/use-toast.ts            ← toast state hook + reducer
   ├─ lib/
   │  ├─ utils.ts                   ← cn() helper (clsx + twMerge)
   │  ├─ store/auth-store.ts        ← Zustand auth store
   │  └─ api/
   │     ├─ client.ts               ← openapi-fetch client + CSRF middleware
   │     └─ schema.d.ts             ← auto-generated by pnpm gen:api
   └─ features/
      ├─ storefront/HomePage.tsx    ← / route
      └─ admin/AdminHomePage.tsx    ← /admin route (RoleGuard'd)
```

### Per-file purpose

#### `tailwind.config.ts`

Tailwind 3.4 config. Maps semantic color tokens (`bg-primary`, `text-foreground`, etc.) to CSS variables defined in `src/index.css`:

```ts
colors: {
  primary: {
    DEFAULT: 'hsl(var(--primary))',
    foreground: 'hsl(var(--primary-foreground))',
  },
  // ...
}
```

**Why the `hsl(var(--token))` indirection?** Two big wins:
1. Dark mode = swap CSS variables in one place (the `.dark { ... }` block in `index.css`). No `dark:bg-X` utilities everywhere.
2. shadcn/ui components copy in with zero edits — they reference these exact token names.

#### `src/index.css`

Tailwind directives + design tokens:

```css
@tailwind base;
@tailwind components;
@tailwind utilities;

@layer base {
  :root { --background: 0 0% 100%; --foreground: 222.2 84% 4.9%; /* ... */ }
  .dark { --background: 222.2 84% 4.9%; --foreground: 210 40% 98%; /* ... */ }
  body { @apply bg-background text-foreground font-sans antialiased; }
}
```

CSS variables are written as **raw HSL channels** (no `hsl(...)` wrapper) so Tailwind can append `/alpha` at the utility level: `bg-primary/50` actually works.

#### `src/lib/utils.ts` — the `cn()` helper

```ts
import { clsx } from 'clsx'
import { twMerge } from 'tailwind-merge'

export function cn(...inputs) {
  return twMerge(clsx(inputs))
}
```

**Why both `clsx` and `twMerge`?**
- `clsx` handles **conditional joining**: `cn('a', cond && 'b')` → `'a b'` or `'a'`
- `twMerge` resolves **Tailwind conflicts**: `cn('p-4', 'p-2')` collapses to `'p-2'`. Without it, both classes ship and the cascade order decides unpredictably.

Every shadcn component uses `cn()` so callers can override defaults without specificity battles.

#### `src/components/ui/button.tsx`

shadcn-style Button. Two patterns to know:

1. **`asChild` + Radix `Slot`** — lets you render the button's styles ON a child element:
   ```tsx
   <Button asChild><Link to="/foo">Go</Link></Button>
   ```
   Without this, you'd nest `<button><a>...</a></button>` — broken keyboard nav + a11y warnings.

2. **`cva` (class-variance-authority)** — turns variant props into type-safe Tailwind class strings:
   ```ts
   const buttonVariants = cva('base-classes', {
     variants: {
       variant: { default: '...', destructive: '...', outline: '...' },
       size: { default: '...', sm: '...', lg: '...' },
     },
     defaultVariants: { variant: 'default', size: 'default' },
   })
   ```
   TypeScript refuses invalid `variant="x"` at compile time. Without cva you'd write conditional concatenation by hand and lose type safety.

#### `src/components/ui/card.tsx`

Compound component pattern — Card, CardHeader, CardTitle, CardDescription, CardContent, CardFooter exported separately. Caller composes only what they need:

```tsx
<Card>
  <CardHeader><CardTitle>...</CardTitle></CardHeader>
  <CardContent>...</CardContent>
  <CardFooter>...</CardFooter>
</Card>
```

**Why compound vs single component with title/subtitle props?** Three reasons:
1. Caller doesn't have to thread a million props
2. Each piece owns its spacing/typography — consistency by construction
3. You can omit pieces you don't need (no footer? skip it)

#### `src/components/ui/toast.tsx` + `toaster.tsx` + `src/hooks/use-toast.ts`

Three files because each layer is independently testable:
- `toast.tsx` — Radix Toast primitives (styled wrappers)
- `use-toast.ts` — the state hook + module-scoped reducer
- `toaster.tsx` — singleton mount point that reads the hook and renders

**Why state at module scope** (not inside a React Context)? So `toast({ ... })` calls work from non-React code (an axios interceptor, an error boundary's `componentDidCatch`). A Context would force every caller to be a component.

#### `src/app/providers.tsx`

```tsx
const [queryClient] = useState(() => new QueryClient({
  defaultOptions: { queries: { staleTime: 30_000, retry: 1, refetchOnWindowFocus: false } },
}))
```

**Why `useState(() => new QueryClient(...))`** instead of `const qc = new QueryClient(...)` at module scope?
- Module-scope is fine for CSR (single client).
- Module-scope **breaks under SSR** — the same QueryClient gets reused across requests, leaking one user's cache to another.

You're CSR-only today, but this future-proofs against an SSR pivot.

**Why `staleTime: 30_000`?** Queries are "fresh" for 30 seconds — so flipping a tab and back doesn't refetch every product list. Tune up for read-mostly data, down for hot data.

#### `src/lib/store/auth-store.ts`

Zustand store. Holds `user`, `isLoading`. Provides `setUser`, `setLoading`. Plus a `getCurrentUser()` non-React helper.

**Why Zustand and not React Context?** Two reasons:
1. **Performance**: Context re-renders ALL subscribers on every change. Zustand's `useAuthStore((s) => s.user)` selector only re-renders if the selected slice changed.
2. **Non-React access**: `useAuthStore.getState().user` works from interceptors and event handlers. Context can't do that without hacks.

**Important security note**: this store does NOT hold the JWT. The JWT is in an HTTP-only cookie that JS can never read (ADR-0007). The store holds only the safe-to-read profile fields.

#### `src/app/guards/RoleGuard.tsx`

```tsx
if (isLoading) return null                              // 1. wait for hydration
if (!user) return <Navigate to={redirectTo} ... />      // 2. anonymous → redirect
if (allowedRoles.length > 0 && !user.roles.some(r => allowedRoles.includes(r)))
  return <Navigate to={redirectTo} replace />            // 3. wrong role → redirect
return <>{children}</>                                   // 4. authorized
```

**Why return `null` during `isLoading`?** On first page load, the app calls `/api/auth/me` to figure out who's logged in. While that's in flight, the store has `user: null, isLoading: true`. Rendering during that window would flash the public state for half a second, then redirect — ugly. `null` is a visual no-op.

**Why this is UX-only, not security?** Frontend authz is never trusted. The BACKEND `[Authorize(Roles = "Admin")]` is the source of truth. RoleGuard just prevents UI flashes; a determined attacker bypasses it trivially. The backend doesn't care.

#### `src/app/router.tsx`

```tsx
export const router = createBrowserRouter([
  { path: '/', element: <HomePage /> },
  { path: '/admin', element: <RoleGuard allowedRoles={['Admin', 'StoreManager']}><AdminHomePage /></RoleGuard> },
])
```

**Why `createBrowserRouter` (the data router) and not `<BrowserRouter>`?** The data router unlocks `loader`, `action`, and deferred data. Even though TanStack Query owns most data fetching, route-level loaders are still the right place for auth checks. Starting on the data router future-proofs.

#### `src/lib/api/client.ts` + `schema.d.ts`

`schema.d.ts` is auto-generated by `pnpm gen:api`, which runs `openapi-typescript` against the API's Swagger doc. The output is a single `paths` type that wires every endpoint → request → response.

`client.ts` uses `openapi-fetch` with `createClient<paths>()`. Calling `apiClient.GET('/api/orders/{id}', { params: { path: { id } } })` is fully typed; a backend contract change becomes a TypeScript compile error.

**The CSRF middleware** (read this twice):
```ts
const csrfMiddleware: Middleware = {
  async onRequest({ request }) {
    if (STATE_CHANGING_METHODS.has(request.method.toUpperCase())) {
      const token = readCookie('XSRF-TOKEN')
      if (token) request.headers.set('X-XSRF-TOKEN', token)
    }
    return request
  },
}
```

**Why this is the right defense:**
- The JWT lives in an HTTP-only cookie that JS can't read — but the browser auto-attaches it on same-origin requests, INCLUDING from a malicious cross-origin form. That's textbook CSRF.
- The defense is **double-submit**: backend sets a SEPARATE, non-httpOnly cookie `XSRF-TOKEN` with a random value. SPA reads it from `document.cookie`, echoes it as `X-XSRF-TOKEN` header on state-changing requests.
- An attacker's site can't read your cookies (same-origin policy on JS cookie reads), so they can't echo the right token.
- GETs don't need the header because they're cache-friendly and idempotent — no state changes to defend.

#### `src/main.tsx`

```tsx
<StrictMode>
  <AppProviders>
    <RouterProvider router={router} />
    <Toaster />
  </AppProviders>
</StrictMode>
```

Order matters:
- `StrictMode` outermost: catches lifecycle bugs by double-rendering in dev.
- `AppProviders` next: routes inside can use TanStack Query immediately.
- `Toaster` as sibling of `RouterProvider`: toasts persist across route changes (independent of which route is active).

### Story 0.3 — what to know cold

1. **shadcn pattern = Radix + cva + cn**. Radix gives a11y, cva gives type-safe variants, cn() merges class strings without conflict.
2. **CSS variables drive theming**, not Tailwind utility duplication. Dark mode = swap `:root` to `.dark`.
3. **TanStack Query = server state. Zustand = client state.** Don't put fetched data in Zustand.
4. **CSRF double-submit cookie** is what protects the httpOnly-JWT pattern.
5. **`useState(() => new QueryClient(...))`** even though you don't need it today — it's the SSR-safe pattern.
6. **`openapi-typescript` → `paths` type → `openapi-fetch`** is the typed-client contract. Contract drift becomes a compile error.

---

## 4. Story 0.4 — Docker local dev

### What's in `docker/`

```
docker/
├─ docker-compose.yml      ← 4 services: sqlserver, azurite, api, web
├─ api/Dockerfile          ← multi-stage backend
├─ web/Dockerfile.dev      ← Vite dev server with HMR
└─ .env.example            ← MSSQL_SA_PASSWORD, JWT_*, AZURITE_*
```

Plus at repo root: `.dockerignore` (excludes node_modules, bin/, obj/, etc.)

### Per-file purpose

#### `docker/api/Dockerfile` — multi-stage build

Two stages:

**Stage 1 (build)** — `mcr.microsoft.com/dotnet/sdk:10.0`
- Copies csproj files FIRST → runs `dotnet restore` (cached layer)
- Then copies all source → runs `dotnet publish`

**Stage 2 (runtime)** — `mcr.microsoft.com/dotnet/aspnet:10.0`
- Smaller image (~220 MB vs ~1.2 GB SDK)
- Runs as non-root `app` user (UID 1654)
- `EXPOSE 8080` (non-root can't bind 80)
- `HEALTHCHECK` curls `/health/live`

**Why csproj-first layering?** Docker layer caching. If only source changed, the restore layer is reused → `docker build` goes from ~90s to ~15s.

**Why non-root user?** Running as root in a network-exposed container is the textbook footgun. The aspnet image pre-creates `app` for exactly this.

#### `docker/web/Dockerfile.dev`

Node 20 alpine + corepack-enabled pnpm + `pnpm install --frozen-lockfile`. Mount source as a volume at runtime so HMR works.

**Why corepack instead of `npm i -g pnpm`?** Corepack ships with Node 16+ and uses the EXACT pnpm version pinned in `package.json`'s `packageManager` field (if present). No "works on my machine because I have pnpm 8 globally" drift.

#### `docker/docker-compose.yml`

Four services on the auto-created bridge network:

| Service | Image / build | Healthcheck | Volume | Port |
|---|---|---|---|---|
| `sqlserver` | mssql/server:2022-latest | sqlcmd SELECT 1 | mssql-data | 1433 |
| `azurite` | azure-storage/azurite | none | azurite-data | 10000-10002 |
| `api` | built from `api/Dockerfile` | wget /health/live | none | 5124→8080 |
| `web` | built from `web/Dockerfile.dev` | none | source bind + anonymous node_modules | 5173 |

**Key compose features used:**
- `depends_on: { sqlserver: { condition: service_healthy } }` — api waits for SQL to be ready, not just "started"
- `ConnectionStrings__Default` env var — ASP.NET reads `__` as a section separator
- `volumes: [ ../src/web:/app, /app/node_modules ]` — the anonymous volume **protects** the container's installed deps from being shadowed by the host's empty node_modules

**Web service is OPTIONAL.** Most frontend devs run Vite on host (faster HMR). Skip it with: `docker compose up -d sqlserver azurite api` (names services explicitly).

#### `docker/.env.example` (not `.env`)

Lists every env var. The committed file has dev defaults; `cp docker/.env.example docker/.env` to create the local file. `.env` is gitignored.

**Why is `.env` next to the compose file?** Compose v2's default discovery looks there. Moving it elsewhere requires `--env-file <path>` on every command — silent failure if you forget.

#### `.dockerignore` (repo root)

Excludes node_modules, bin/, obj/, dist/, docs/, .env, secrets.json. **Critical for build speed**: without it, `docker build .` ships your entire `node_modules` (200+ MB) into the build context for every build, wasting time and risking stale artifacts in image layers.

### Story 0.4 — what to know cold

1. **Multi-stage builds: SDK builds, runtime serves.** Final image carries no compilers.
2. **csproj-first COPY** is the layer-caching trick that makes incremental builds fast.
3. **`depends_on: condition: service_healthy`** waits for the dependency to be **actually ready**, not just running.
4. **Anonymous volume for `node_modules`** is how you do bind-mount + installed-deps without one wiping the other.
5. **`.dockerignore` is mandatory**, not optional. Without it the build context bloats catastrophically.

---

## 5. Story 0.5 — CI/CD + Bicep IaC

### What's in `.github/workflows/` and `infra/bicep/`

```
.github/workflows/
├─ ci.yml                       ← 3 parallel jobs on every PR
└─ iac.yml                      ← bicep build + manual what-if

infra/bicep/
├─ main.bicep                   ← targetScope=subscription, creates RG
├─ bicepconfig.json             ← downgrades no-unused-params to Info
├─ modules/                     ← 12 placeholder modules (one per resource group of concerns)
│  ├─ monitoring.bicep          ← Phase 9: Log Analytics + App Insights
│  ├─ keyVault.bicep            ← Phase 1: secrets
│  ├─ registry.bicep            ← Phase 0/11: ACR
│  ├─ sql.bicep                 ← Phase 1: Serverless DB
│  ├─ storage.bicep             ← Phase 4: blob storage
│  ├─ ai.bicep                  ← Phase 4: Azure AI Language (sentiment)
│  ├─ containerApps.bicep       ← Phase 11: deploy API
│  ├─ staticWebApp.bicep        ← Phase 11: deploy SPA
│  ├─ serviceBus.bicep          ← Phase 8: queues + topics
│  ├─ eventGrid.bicep           ← Phase 8: pub/sub
│  ├─ functions.bicep           ← Phase 8: event consumers
│  └─ apim.bicep                ← Phase 11: API gateway
└─ env/
   ├─ dev.bicepparam            ← env='dev', location='australiaeast'
   └─ prod.bicepparam           ← env='prod', location='australiaeast'
```

### Per-file purpose

#### `.github/workflows/ci.yml`

Three parallel jobs:

**`api`** — `actions/setup-dotnet@v4` with `10.0.x` + NuGet cache + restore/build/test + upload trx.

**`web`** — `pnpm/action-setup@v4` + `setup-node@v4` with pnpm cache + install + typecheck + lint + format:check + build.

**`bicep`** — `az bicep install` (idempotent) + compile main.bicep + compile each module + compile bicepparam.

**Key features:**
- `concurrency` group with `cancel-in-progress: true` — when you push a new commit to a PR, the previous stale CI run is cancelled. Saves minutes.
- NuGet cache keyed on `hashFiles('**/*.csproj')` — a csproj change busts the cache cleanly.
- `if: always()` on test upload — gets the .trx even on failure for post-mortem.

#### `.github/workflows/iac.yml`

Two jobs:

**`build`** — runs on every PR/push touching `infra/bicep/**` AND on manual dispatch. Pure syntax check; no Azure auth.

**`what-if`** — gated `if: github.event_name == 'workflow_dispatch'`. Only runs when manually triggered. Uses `azure/login@v2` with OIDC.

**Why the gate?** You haven't set up the federated credential yet. Without the gate, PRs would go red on the missing secrets. With it, `bicep build` keeps PRs green; when you provision Azure later, no workflow edit needed — just trigger from the Actions tab.

**OIDC setup checklist (for when you're ready):**
1. Create Azure AD App Registration `retail-oms-gha`
2. Add a federated credential targeting `repo:<org>/retail-order-management-system:ref:refs/heads/main`
3. Grant the SP `Contributor` + `User Access Administrator` at subscription scope
4. Add repo secrets: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`

**Why OIDC instead of a service-principal password?** No long-lived secret in your repo. The federation trusts GitHub's signed token; rotation happens automatically.

#### `infra/bicep/main.bicep`

```bicep
targetScope = 'subscription'

@allowed(['dev', 'prod']) param env string
param location string = 'australiaeast'

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-retail-${env}'
  location: location
  tags: { project: 'Retail OMS', env: env, managedBy: 'Bicep' }
}

// All 12 module activations are commented out — uncomment per phase.
// module monitoring 'modules/monitoring.bicep' = { scope: rg, ... }
```

**Why `targetScope = 'subscription'`?** Because we want THIS template to create the RG. Subscription-scope deployments can include child RG-scope modules (via `scope: rg`); the reverse isn't true.

**Why are module activations commented out?** Empty modules compile, but a `module x ... { params: {...} }` invocation requires param wiring. Commenting out the invocations means main.bicep compiles today, CI passes today, and you activate each module one PR at a time as its resources land.

#### `infra/bicep/modules/*.bicep`

Each module follows the same template:
```bicep
@description('Azure region.')
param location string = resourceGroup().location

@description('Environment short name.')
param env string

@description('Common tags.')
param tags object = {}

// TODO Phase N: declare resources here.
```

Plus a header comment explaining what lands there in which phase + the SKU/tier tradeoff (e.g., "APIM Consumption tier because Developer is $50/month idle").

#### `infra/bicep/bicepconfig.json`

Bicep linter config. Downgrades `no-unused-params` to Info-level — placeholder modules legitimately have unused params; suppressing the warning prevents noisy CI logs. The warning becomes a real signal again when modules have resources.

#### `infra/bicep/env/*.bicepparam`

Per-environment param files. Replaces the legacy `parameters.json` shape — `.bicepparam` files are **statically type-checked against main.bicep at build time**. A renamed param fails compile instead of failing deployment.

```bicep
using '../main.bicep'   // ← this link makes type-checking possible

param env = 'dev'
param location = 'australiaeast'
```

### Story 0.5 — what to know cold

1. **`concurrency: cancel-in-progress` saves CI minutes** and stops the "wait for the failed run to finish first" cycle.
2. **`workflow_dispatch` gating** keeps "we're not ready yet" workflows green without disabling them.
3. **Bicep targetScope tree**: subscription > resourceGroup. Subscription-scope can create RGs and call RG-scope modules; not vice versa.
4. **`bicepparam` is type-checked at compile time** against the `main.bicep` it points at via `using`.
5. **OIDC = no long-lived secrets.** Federated credential trusts GitHub's signed identity assertion.

---

## 6. File relationship maps

### Backend request flow

```
HTTP request to /api/health
   │
   ▼
ExceptionMiddleware.InvokeAsync(context)  ← try wraps everything below
   │
   ▼
SerilogRequestLogging              ← one structured log line per request
   │
   ▼
SwaggerUI? (dev only — short-circuits at /swagger)
   │
   ▼
UseRouting                         ← matches /api/health to HealthController.Get
   │
   ▼
UseAuthentication                  ← validates JWT (skipped for AllowAnonymous)
UseAuthorization                   ← checks roles (skipped for AllowAnonymous)
   │
   ▼
HealthController.Get([FromServices] IWebHostEnvironment env)
   │
   ├─ creates HealthPayload { Service, Environment, UtcNow }
   ├─ wraps in ApiResponse<HealthPayload>.Ok(payload, "Service is healthy")
   │     └─ TraceId auto-populated from Activity.Current (OpenTelemetry)
   └─ return Ok(envelope)
   │
   ▼
MVC serializes ApiResponse<HealthPayload> to JSON
   │ (camelCase + ignore-null per Program.cs JsonOptions)
   ▼
HTTP 200 + body { "success": true, "data": {...}, "traceId": "...", ... }
```

### DbContext + AuditingInterceptor

```
Controller calls Service
   │
   ▼
Service calls dbContext.SaveChangesAsync()
   │
   ▼
EF Core fires SavingChangesAsync interceptors
   │
   ▼
AuditingInterceptor.SavingChangesAsync(eventData, ...)
   │
   ├─ calls Stamp(eventData.Context)
   │     ├─ resolves now = timeProvider.GetUtcNow()
   │     ├─ resolves userId = httpContext.User.FindFirst(NameIdentifier)?.Value
   │     │
   │     └─ for each ChangeTracker.Entries<IAuditableEntity>():
   │           ├─ Added   → entity.CreatedAt = now; entity.CreatedBy = userId
   │           └─ Modified → entity.UpdatedAt = now; entity.UpdatedBy = userId
   │                        + mark CreatedAt/CreatedBy as IsModified=false
   │
   └─ returns base.SavingChangesAsync(eventData, result, ct)
   │
   ▼
EF Core generates SQL with the now-stamped audit columns
```

**Note**: no entity implements `IAuditableEntity` in Phase 0, so the interceptor is a no-op today. It activates the moment Phase 1+ adds a domain entity with the interface.

### Frontend mount → first API call

```
main.tsx mounts <StrictMode><AppProviders><RouterProvider /><Toaster /></AppProviders></StrictMode>
   │
   ▼
AppProviders creates QueryClient → wraps children in <QueryClientProvider>
   │
   ▼
RouterProvider matches "/" → renders <HomePage />
   │
   ▼
HomePage uses Button, Card, Input, useToast — all driven by cn() + Tailwind tokens from index.css
   │
   ▼
User clicks "Go to /admin" — React Router navigates
   │
   ▼
<RoleGuard allowedRoles={['Admin', 'StoreManager']}>
   │
   ├─ useAuthStore() returns { user: null, isLoading: true }
   ├─ isLoading=true → return null (visual no-op)
   ├─ ...time passes; /api/auth/me responds...
   ├─ setUser({...}) → isLoading=false, user populated
   ├─ user.roles doesn't include 'Admin' or 'StoreManager' → <Navigate to="/" />
   └─ ...or user has the role → renders <AdminHomePage />
```

### Frontend → backend with CSRF

```
Component calls apiClient.POST('/api/orders', { body: ... })
   │
   ▼
openapi-fetch builds the Request object
   │
   ▼
csrfMiddleware.onRequest({ request })
   │
   ├─ method = 'POST' (state-changing) → read 'XSRF-TOKEN' cookie
   ├─ token found → request.headers.set('X-XSRF-TOKEN', token)
   │
   └─ return request
   │
   ▼
fetch() sends request with:
   - cookie: JWT (httpOnly, sent automatically by browser)
   - cookie: XSRF-TOKEN (readable, sent automatically by browser)
   - header: X-XSRF-TOKEN (echoed from cookie by middleware)
   │
   ▼
Backend (Phase 1+):
   - JwtBearer middleware validates JWT cookie → sets HttpContext.User
   - CSRF middleware (Phase 1) compares cookie XSRF-TOKEN vs header X-XSRF-TOKEN → 403 on mismatch
   - Controller executes
```

### docker-compose service network

```
docker network ("retail_default", auto-created)
   │
   ├─ sqlserver   (hostname: sqlserver, port 1433 inside network)
   │     │
   │     ▼
   ├─ api         (hostname: api, port 8080 inside network, 5124 on host)
   │     │ ConnectionStrings__Default = "Server=sqlserver,1433;Database=RetailOms;..."
   │     │
   │     ▼ depends_on: { sqlserver: condition: service_healthy }
   ├─ web         (hostname: web, port 5173 inside network, 5173 on host)
   │     │ proxies /api/* → api:8080 (via vite.config.ts dev proxy)
   │     │
   └─ azurite     (hostname: azurite, ports 10000/10001/10002)
                  └─ available for Phase 4+ blob storage
```

### Bicep module activation flow (future)

```
PR adds Phase 1 resources to modules/sql.bicep
   │
   ▼
Same PR uncomments the corresponding block in main.bicep:
   module sql 'modules/sql.bicep' = {
     scope: rg
     name: 'sql'
     params: { location: location, env: env, tags: tags }
   }
   │
   ▼
CI (ci.yml bicep job) compiles main.bicep transitively → catches param mismatches
   │
   ▼
Manual `workflow_dispatch` on iac.yml → bicep what-if shows the diff
   │
   ▼
Merge → cd-staging.yml (Phase 11) applies the deployment
```

---

## 7. Patterns to remember

These are the things to be able to whiteboard in an interview, in priority order:

### 1. ApiResponse envelope (highest priority)

**The shape:**
```jsonc
{ success, data, message, errors[], traceId, timestamp }
```

**Why:** Frontend has ONE response interceptor instead of branching on every endpoint + every error source (ProblemDetails, FluentValidation dict, empty body, etc.).

**Resume claim:** "Standardized response envelope with W3C trace ID for end-to-end observability correlation."

### 2. Global ExceptionMiddleware

**The pattern:** Conventional middleware (RequestDelegate ctor) → try/catch around `_next(context)` → switch expression maps exception type to (status, code, message) → serialize ApiResponse.

**Why:** DRY across 50+ controllers; catches model binding + filter exceptions controllers can't see; one place to change wire format.

**Interview gotcha:** Why hide stack traces in prod? Stack traces leak SQL fragments, file paths, library versions — CVE bait.

### 3. SaveChangesInterceptor pattern

**The pattern:** Override both `SavingChanges` (sync) AND `SavingChangesAsync` (async) → route to common `Stamp()` helper → walk `ChangeTracker.Entries<IAuditableEntity>()` → set audit fields based on `EntityState`.

**Why:** DRY across every entity + catches all SaveChanges paths (controllers, bg services, migrations).

**Interview gotcha:** Why mark `CreatedAt.IsModified = false` on UPDATE? Defensive — protects immutable created fields from a buggy caller that re-sets them.

### 4. IdentityDbContext + IEntityTypeConfiguration scan

**The pattern:**
```csharp
public class RetailDbContext : IdentityDbContext<ApplicationUser> {
  protected override void OnModelCreating(ModelBuilder b) {
    base.OnModelCreating(b);                                            // Identity tables
    b.ApplyConfigurationsFromAssembly(typeof(RetailDbContext).Assembly); // domain configs
  }
}
```

**Why:** OnModelCreating stays a one-liner forever, regardless of entity count. New entity = drop a Configuration class in `Data/Configurations/`, no DbContext edit.

### 5. shadcn pattern (Radix + cva + cn)

- **Radix** = a11y-correct headless primitives
- **cva** = type-safe variant-to-class mapping
- **cn** = clsx (conditional) + twMerge (conflict resolution) for class strings

**Resume claim:** "Hand-built shadcn/ui component library on Radix primitives + class-variance-authority for type-safe variants."

### 6. TanStack Query (server state) vs Zustand (client state)

**Rule:** anything that came from an HTTP call = TanStack Query. Anything else = Zustand (or component state).

**Why split?** TanStack handles cache invalidation, refetch policies, optimistic updates, race conditions on its own. Zustand doesn't try to be smart — it's just a global store. Putting server data in Zustand means re-implementing every TanStack feature poorly.

### 7. HttpOnly JWT cookie + CSRF double-submit

**Why httpOnly cookie + not localStorage:**
- XSS can read localStorage → token stolen → game over
- HttpOnly cookie is unreadable from JS → XSS can't steal the JWT directly

**Why double-submit CSRF:**
- HttpOnly cookie auto-sends on cross-site malicious form submits (browser doesn't know)
- Backend issues a SEPARATE cookie `XSRF-TOKEN` (readable)
- SPA echoes it as `X-XSRF-TOKEN` header on POST/PUT/PATCH/DELETE
- Attacker can't read your cookies → can't echo the right token → CSRF blocked

### 8. Multi-stage Docker build

**The pattern:** SDK stage builds + publishes; runtime stage copies only the publish output. Non-root user for runtime.

**Layer caching trick:** COPY csproj first → `dotnet restore` → COPY source → `dotnet publish`. Source-only edits don't bust the restore layer.

### 9. OIDC federation for GitHub Actions

**The pattern:** AD App Registration + federated credential trusting `repo:org/repo:ref:refs/heads/main` + repo secrets for IDs (no client secret).

**Why:** No long-lived secrets in your repo. Rotation is automatic. The GitHub-signed identity assertion is what Azure trusts.

### 10. Bicep `targetScope = 'subscription'`

**Why:** Lets THIS template create the resource group, then deploy modules into it (`scope: rg`). The reverse (RG-scope creating sub-scope resources) isn't possible.

---

## 8. What's next

### Phase 1 will activate everything Phase 0 wired

| Phase 0 wire | Phase 1 activation |
|---|---|
| `IAuditableEntity` interface | First domain entity (probably `Product`) implements it → AuditingInterceptor starts stamping |
| `ApiResponse<T>` envelope | Real endpoints (login, register, refresh) return it |
| `RetailDbContext` IdentityDbContext | First real `dbContext.Users` query lands |
| JWT Bearer auth | Login endpoint issues tokens; protected endpoints get `[Authorize]` |
| CSRF middleware (frontend) | Backend CSRF middleware lands; double-submit validation goes live |
| `RoleGuard` frontend | First admin route uses real role claims |
| `apiClient` typed client | First real call: `apiClient.POST('/api/auth/login', {...})` |
| Health checks `/health/ready` | DB check actually means something once SQL Server is running |

### Open question before Phase 1

You renamed roles to `store manager / staff / administrator` for the resume bullet. But the **customer-facing role** (currently "Customer" in the memory) — keep as "Customer" or rename to match the same family?

Pros of "Customer": clear, customer-facing terminology stays separate from internal roles.
Pros of renaming: consistency across all roles in code.

Decide before Phase 1 starts — the role enum gets written early and shows up everywhere.

### Where to look up things later

- **"What did Phase 0 build?"** → this file
- **"What does this specific file do?"** → comment block at the top of every file (you wrote them heavy on purpose)
- **"Why did we choose X?"** → `docs/adr/<NNNN>-<topic>.md`
- **"What's the current task?"** → memory's `project_progress.md`
- **"What's the locked stack?"** → memory's `tech_decisions.md` + `docs/PLAN.md` §5
