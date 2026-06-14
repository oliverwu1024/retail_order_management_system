# ADR-0007: JWT in HTTP-only Cookies (not localStorage) + Refresh-Token Rotation + Signed Double-Submit CSRF

**Status**: Accepted (2026-06-14)

**Deciders**: project owner

**Related**: `REQUIREMENTS.md` §1.1 (认证) · `CODING_STANDARDS.md` §认證 cookie 約定 · PLAN.md §13 Phase 1 · `Identity/JwtService.cs` · `Identity/CsrfTokenService.cs` · `Middlewares/CsrfMiddleware.cs` · `Controllers/AuthController.cs` · résumé bullet Job B-2 ("HTTP-only cookies and refresh-token rotation on top of JWT authentication")

---

## Context

The SPA needs an authenticated session against the ASP.NET Core API. The dominant tutorial pattern — mint a JWT, return it in the response body, store it in `localStorage`, attach it as `Authorization: Bearer` — has one disqualifying weakness for a portfolio that wants to *demonstrate* security judgement: **any XSS on the page can read `localStorage` and exfiltrate the token.** The token is bearer; whoever holds it is the user, for its full lifetime, from anywhere.

This ADR records the authentication design the implementation follows. It is the artifact `REQUIREMENTS.md` §1.1 points at ("詳見 `docs/adr/0007-jwt-cookie-vs-localstorage.md`") and the evidence behind the Job B-2 résumé bullet. Three decisions compound: **where the token lives**, **how sessions are kept alive without long-lived bearer tokens**, and **how the resulting cookie auth is defended against CSRF**.

## Decision

### 1. The access JWT lives in an HTTP-only cookie, not localStorage

Login/register/refresh **never return a token in the response body** (the body carries only the user profile). Instead the server issues:

- `access_token` — the JWT — as `HttpOnly; Secure; SameSite=Strict; Path=/`, **15-minute** lifetime.

`HttpOnly` means JavaScript (including injected XSS) cannot read the token. The API extracts the JWT from this cookie via a `JwtBearerEvents.OnMessageReceived` hook (so the existing `AddJwtBearer` validation — issuer/audience/lifetime/signing-key — is reused verbatim; only the *transport* changes from header to cookie). The browser attaches the cookie automatically; the SPA never sees, stores, or forwards the token.

### 2. Refresh-token rotation with reuse detection — short access tokens, revocable sessions

A 15-minute access token expiring mid-session is bad UX; a long-lived one is a bigger blast radius. We split the two concerns:

- `refresh_token` — an **opaque, cryptographically-random** value (not a JWT) — as `HttpOnly; Secure; SameSite=Strict; Path=/`, **14-day** lifetime. Only its **SHA-256 hash** is stored server-side (`RefreshTokens` table). A DB leak therefore exposes no usable token.
- `POST /api/v1/auth/refresh` validates the presented refresh token, then **rotates** it: the old token is revoked (`RevokedAt`, `ReplacedByTokenHash`, `ReasonRevoked`) and a fresh access+refresh pair is issued. A refresh token is single-use.
- **Reuse detection**: if a token that was *already revoked* (i.e. already rotated away) is presented again, that is the signature of a stolen-and-replayed token. The response is to **revoke every active refresh token for that user** — nuking all sessions — and reject the request. The legitimate user re-authenticates; the thief's stolen token is now worthless.

This is OWASP-recommended refresh-token rotation (RTR). It is the substance of the Job B-2 bullet.

### 3. CSRF defense: SameSite=Strict (primary) + signed double-submit token (defense-in-depth)

Cookie auth re-introduces CSRF risk (the browser attaches the auth cookie to forged cross-site requests). Two layers:

- **`SameSite=Strict`** on the auth cookies is the primary defense: the browser will not attach them to *any* cross-site request, so a forged request from `evil.com` carries no credentials at all.
- **Signed double-submit token** as defense-in-depth and to satisfy the explicit PRD contract. `GET /api/v1/auth/csrf` issues a `csrf` cookie (**non-HttpOnly**, so the SPA can read it) whose value is `random.HMAC(random, serverKey)`. Every state-changing request must echo that value in the **`X-CSRF-Token`** header; `CsrfMiddleware` rejects the request (403) unless the header is present, equals the cookie, **and** carries a valid HMAC signature.

The HMAC signature is what makes this robust rather than naive double-submit: an attacker who can *plant* a cookie (sibling-subdomain XSS, http MITM) still cannot forge a value with a valid signature, because the server key is secret. This recovers the cryptographic-binding property of ASP.NET's `IAntiforgery` while keeping the mechanism fully transparent and a clean fit for a stateless JWT-cookie SPA (see Alternatives).

## Consequences

**Positive**

- **XSS cannot steal the session.** `HttpOnly` puts the access token out of JavaScript's reach — the single biggest reason to prefer cookies over `localStorage`.
- **Short access-token blast radius + revocable sessions.** A leaked 15-minute access token expires fast; rotation makes refresh tokens single-use; reuse detection turns token theft into an automatic global logout.
- **No DB-leak token exposure.** Only SHA-256 hashes of refresh tokens are stored.
- **Interview-defensible, line-by-line.** Every mechanism (cookie flags, rotation, HMAC double-submit) is hand-written and explainable — the whole point of the portfolio.

**Negative / trade-offs**

- **`SameSite=Strict` breaks across registrable domains.** If the SPA (e.g. `*.azurestaticapps.net`) and the API (e.g. `*.azurecontainerapps.io`) end up on different registrable domains, Strict cookies are never sent and auth silently fails. **Mitigation / revisit at Phase 6**: front both behind one registrable domain via APIM custom domains (`app.example.com` + `api.example.com` are same-site), keeping Strict. Relaxing to `SameSite=None; Secure` is the fallback only if same-site cannot be arranged.
- **`Secure` cookies require HTTPS.** Local plain-http testing (docker-compose over http) would drop the cookies. **Mitigation**: `Auth:SecureCookies` config flag — `false` in `appsettings.Development.json`, `true` in production.
- **CSRF bootstrap step.** The SPA must `GET /auth/csrf` once before its first state-changing call. Handled centrally in the `apiClient` interceptor (see CODING_STANDARDS §认證 cookie 約定).
- **More moving parts than `localStorage`.** A refresh table, a rotation path, a CSRF middleware. Accepted: the complexity *is* the demonstrated competency.

## Alternatives considered

1. **JWT in `localStorage` + `Authorization: Bearer`** — Rejected. XSS-readable; the exact weakness this design exists to remove.
2. **Access token in JS memory + refresh token in an HttpOnly cookie** — A reasonable middle ground (access token dies on reload, refresh token is protected). Rejected for this project because keeping the access token in JS still exposes it to XSS for its lifetime, and the pure-cookie model is the stronger, more demonstrable story. Locked in `tech_decisions.md`.
3. **ASP.NET Core `IAntiforgery` for CSRF** — The framework's built-in, cryptographically-bound antiforgery. Rejected as the primary mechanism: it is designed around the auth *cookie*/`ClaimsPrincipal` and server-rendered Razor forms, and bolting it onto a headless JWT-cookie SPA is off-label (notably, the `/csrf` endpoint is hit before login, when there is no principal to bind to). The signed double-submit token recovers its key strength (cryptographic binding via HMAC) with a mechanism that fits the stateless model and is transparent end-to-end.
4. **Naive (unsigned) double-submit** — Rejected. Breaks if an attacker can write a cookie to the domain. The HMAC signature closes that gap for ~5 lines of code.

## Implementation notes

- `Identity/JwtService.cs` — mints the access JWT (claims: `sub`=user id, `email`, `name`, role claims; HS256). `OnMessageReceived` in `Program.cs` reads it from the `access_token` cookie.
- `Identity/CsrfTokenService.cs` — issues/validates the `random.HMAC(random)` token; HMAC key currently reuses `Jwt:Key` (a dedicated `Csrf:Key` can be introduced without changing the wire format).
- `Identity/AuthCookies.cs` — the **one** place all cookie security attributes are set (`HttpOnly`/`Secure`/`SameSite`/`Path`/`Expires`), so the flags are auditable in a single file.
- `Services/AuthService.cs` — register/login/refresh/logout; owns rotation + reuse detection. Returns an `AuthResult` (no `HttpContext` dependency); the controller owns cookie writing.
- `Middlewares/CsrfMiddleware.cs` — enforces the double-submit check on all unsafe HTTP methods.
- Password policy reconciled to PRD §1.1 (**12 chars, ≥1 letter + ≥1 digit**) in both Identity options and the FluentValidation `RegisterRequestValidator`. Supersedes the 8-char policy that was in `Program.cs`.
- `ApplicationUser` gains `DisplayName` (PRD §1.1 registration field) and implements `IAuditableEntity` (account-creation audit trail).

## Revisit triggers

- **Phase 6 deploy** places SPA and API on different registrable domains → arrange same-site via APIM custom domains, or relax to `SameSite=None; Secure`.
- **A dedicated CSRF signing key is wanted** (key-rotation independence from `Jwt:Key`) → add `Csrf:Key`; wire format unchanged.
- **Refresh-token contention** under real load (parallel refreshes racing on rotation) → add a `RowVersion` to `RefreshToken` and handle the concurrency conflict, or a short grace window on the just-rotated token.
- **`Microsoft.AspNetCore.Authentication.BearerToken` / first-party cookie-JWT helpers mature** → re-evaluate hand-rolled pieces against the framework primitives.
