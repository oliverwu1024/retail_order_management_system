import createClient, { type Middleware } from 'openapi-fetch'
import type { paths } from '@/lib/api/schema'

// ─────────────────────────────────────────────────────────────────────────────
//  apiClient — the typed HTTP client for the Retail API.
//
//  WHY openapi-fetch + GENERATED TYPES?
//  ------------------------------------
//  Hand-written API types drift away from the backend the moment someone
//  ships a contract change. With openapi-fetch + openapi-typescript:
//    1. CI runs `pnpm gen:api` against the OpenAPI doc.
//    2. The generated `paths` type wires endpoint → request → response.
//    3. apiClient.GET('/api/orders/{id}', { params: { path: { id } } })
//       fails TypeScript compile if the contract changes underneath it.
//  Endpoint drift becomes a build error, not a runtime 500.
//
//  WHY THE CSRF MIDDLEWARE?
//  ------------------------
//  We use the httpOnly cookie + JWT pattern (ADR-0007): the JWT lives in
//  an HTTP-only cookie that JavaScript can't read. That cookie auto-
//  attaches on same-origin requests, which means the browser sends auth
//  for free — including on cross-site malicious form submits (the
//  textbook CSRF). The defense is double-submit:
//    - Backend sets a NON-httpOnly cookie XSRF-TOKEN with a random value.
//    - SPA reads it from document.cookie and echoes it as X-XSRF-TOKEN
//      header on every state-changing request.
//    - Backend rejects requests whose header doesn't match the cookie.
//  An attacker's site can't read the cookie (same-origin policy on JS
//  cookie reads from another origin), so they can't echo it.
//
//  We attach the header only on POST/PUT/PATCH/DELETE — GETs are
//  cache-friendly and idempotent, no CSRF risk.
// ─────────────────────────────────────────────────────────────────────────────

const STATE_CHANGING_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE'])

function readCookie(name: string): string | null {
  // document.cookie is "k1=v1; k2=v2" — parse for our specific key.
  // Returns null when the cookie isn't set (e.g., first page load before
  // the backend has issued an XSRF-TOKEN).
  const match = document.cookie.match(new RegExp(`(?:^|; )${name}=([^;]*)`))
  return match ? decodeURIComponent(match[1]) : null
}

const csrfMiddleware: Middleware = {
  async onRequest({ request }) {
    if (STATE_CHANGING_METHODS.has(request.method.toUpperCase())) {
      const token = readCookie('XSRF-TOKEN')
      if (token) {
        request.headers.set('X-XSRF-TOKEN', token)
      }
    }
    return request
  },
}

export const apiClient = createClient<paths>({
  // baseUrl is relative so Vite's dev proxy forwards /api/* to the
  // backend (configured in vite.config.ts). In production the SPA and
  // API live behind the same APIM endpoint, so relative URLs work there
  // too — no environment-conditional code.
  baseUrl: '',
  credentials: 'include', // send the httpOnly JWT cookie on every request
})

apiClient.use(csrfMiddleware)
