// ─────────────────────────────────────────────────────────────────────────────
//  apim module — placeholder for Phase 11 (API gateway in front of api).
//
//  WHEN ACTIVATED, deploys:
//    - API Management instance, Consumption tier
//    - api product `retail-api` with the OpenAPI doc imported from the
//      api's exported Swagger
//    - Policies:
//        - rate-limit (per subscription key)
//        - validate-jwt (offloads token validation from the api itself)
//        - cors (locks origins to the staticWebApp hostname + localhost dev)
//    - Backend pointing at the Container App's ingress URL
//
//  WHY CONSUMPTION TIER?
//  --------------------
//  Developer / Basic / Standard / Premium all cost $50-$3,000+ per month
//  for an idle instance. Consumption is per-call and scales to zero — the
//  only tier that doesn't accumulate a bill on a portfolio app. Trade-off:
//  no VNet support, limited cache, no policies-with-context for backend
//  health probes. Acceptable at this scale.
//
//  WHY APIM AT ALL?
//  ----------------
//  Job A bullet "Container Apps behind Azure API Management" is the
//  motivator. APIM brings rate-limiting, central auth, request/response
//  logging, and a developer portal we can demo. Without APIM the resume
//  bullet doesn't have a backbone.
// ─────────────────────────────────────────────────────────────────────────────

@description('Azure region.')
param location string = resourceGroup().location

@description('Environment short name (dev, prod).')
param env string

@description('Common resource tags inherited from main.bicep.')
param tags object = {}

// TODO Phase 11: APIM Consumption, api import from Swagger JSON, policies
// for rate-limit + JWT validation + CORS, backend to Container App ingress.
