import http from 'k6/http'
import { check, sleep, group } from 'k6'
import { Rate } from 'k6/metrics'

// ─────────────────────────────────────────────────────────────────────────────
//  Catalog browse — k6 load baseline (PLAN.md §13 Phase 1 "first perf baseline").
//
//  Models a storefront shopper browsing the (anonymous, public) catalog reads:
//    1. GET /catalog/categories          — the filter panel
//    2. GET /catalog/products?page/filter — the product grid (with paging/category filter)
//    3. GET /catalog/products/{slug}      — a product detail page
//
//  These three endpoints are the entire public read surface and the hot path a
//  storefront hammers. setup() discovers the live slugs + category ids so the
//  script isn't pinned to a specific seed set.
//
//  Run:  BASE_URL=http://localhost:5124 k6 run tests/load/catalog-browse.js
//  Record the summary in docs/perf/baseline-{date}.md.
// ─────────────────────────────────────────────────────────────────────────────

const BASE_URL = __ENV.BASE_URL || 'http://localhost:5124'

// Custom rate so a failed CHECK (not just a non-2xx) shows up as an error budget.
const browseErrors = new Rate('browse_errors')

export const options = {
  // Ramp 0→20 VUs, hold, ramp down — ~2 min. A modest concurrency baseline.
  stages: [
    { duration: '30s', target: 20 },
    { duration: '1m', target: 20 },
    { duration: '30s', target: 0 },
  ],
  // The recorded SLOs: future runs (and CI's nightly load test) gate against these.
  thresholds: {
    http_req_failed: ['rate<0.01'], // <1% transport/5xx failures
    http_req_duration: ['p(95)<500'], // 95th percentile under 500ms
    checks: ['rate>0.99'], // >99% of assertions pass
    browse_errors: ['rate<0.01'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
}

// Runs once before the test: discover the catalog so VUs browse real data.
export function setup() {
  const listRes = http.get(`${BASE_URL}/api/v1/catalog/products?Page=1&PageSize=100`)
  const catRes = http.get(`${BASE_URL}/api/v1/catalog/categories`)

  let slugs = []
  let categoryIds = []
  try {
    slugs = (listRes.json().data.items || []).map((p) => p.slug).filter(Boolean)
  } catch (_) {
    slugs = []
  }
  try {
    categoryIds = (catRes.json().data || []).map((c) => c.id).filter(Boolean)
  } catch (_) {
    categoryIds = []
  }

  if (slugs.length === 0) {
    throw new Error(`No products found at ${BASE_URL} — seed the catalog before load testing.`)
  }
  return { slugs, categoryIds }
}

function pick(arr) {
  return arr[Math.floor(Math.random() * arr.length)]
}

export default function (data) {
  group('categories', () => {
    const res = http.get(`${BASE_URL}/api/v1/catalog/categories`)
    browseErrors.add(!check(res, { 'categories 200': (r) => r.status === 200 }))
  })
  sleep(0.5)

  group('product list', () => {
    // Half the iterations filter by a random category (exercises the indexed path).
    const filter =
      data.categoryIds.length > 0 && Math.random() < 0.5
        ? `&CategoryId=${pick(data.categoryIds)}`
        : ''
    const res = http.get(`${BASE_URL}/api/v1/catalog/products?Page=1&PageSize=12${filter}`)
    browseErrors.add(
      !check(res, {
        'list 200': (r) => r.status === 200,
        'list has items array': (r) => {
          try {
            return Array.isArray(r.json().data.items)
          } catch (_) {
            return false
          }
        },
      }),
    )
  })
  sleep(1)

  group('product detail', () => {
    const res = http.get(`${BASE_URL}/api/v1/catalog/products/${pick(data.slugs)}`)
    browseErrors.add(
      !check(res, {
        'detail 200': (r) => r.status === 200,
        'detail has id': (r) => {
          try {
            return r.json().data.id !== undefined
          } catch (_) {
            return false
          }
        },
      }),
    )
  })
  sleep(1)
}
