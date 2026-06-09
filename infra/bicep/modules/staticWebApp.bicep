// ─────────────────────────────────────────────────────────────────────────────
//  staticWebApp module — placeholder for Phase 11 (deploy SPA to Azure).
//
//  WHEN ACTIVATED, deploys:
//    - Azure Static Web Apps (Free tier — sufficient for portfolio scale)
//    - Linked to the GitHub repo via the deployment token (set as a
//      secret in repo settings; not in code)
//    - Custom routes config from src/web/staticwebapp.config.json
//
//  WHY SWA AND NOT BLOB-STATIC-WEBSITE OR ANOTHER CONTAINER APP?
//  ------------------------------------------------------------
//  SWA Free tier gives us: SPA fallback routing, free TLS cert, integrated
//  PR preview environments, brotli compression at the CDN edge. Blob
//  static-website needs a CDN profile + manual cert + custom fallback —
//  more moving parts for no functional gain. Container App would be paying
//  to run nginx to serve static files; absurd.
// ─────────────────────────────────────────────────────────────────────────────

@description('Azure region.')
param location string = resourceGroup().location

@description('Environment short name (dev, prod).')
param env string

@description('Common resource tags inherited from main.bicep.')
param tags object = {}

// TODO Phase 11: Static Web App Free tier, GitHub integration, custom
// hostname (prod only), staticwebapp.config.json shipped from src/web.
