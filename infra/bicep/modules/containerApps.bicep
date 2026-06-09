// ─────────────────────────────────────────────────────────────────────────────
//  containerApps module — placeholder for Phase 11 (deploy api to Azure).
//
//  WHEN ACTIVATED, deploys:
//    - Container Apps Environment (Consumption workload profile)
//    - Container App `retail-api`:
//        - System-assigned managed identity (reads Key Vault, pulls from ACR)
//        - Image pinned to ACR ref injected by cd-staging.yml / cd-prod.yml
//        - HTTP scaling 1→10 replicas (concurrent-requests trigger)
//        - Min replicas 0 in dev (scale-to-zero); 1 in prod (always-on)
//        - Env vars: ConnectionStrings__Default via Key Vault reference,
//          ApplicationInsights__ConnectionString from monitoring module
//
//  WHY CONTAINER APPS AND NOT APP SERVICE / AKS?
//  --------------------------------------------
//  App Service is heavier and pricier per-CPU. AKS is overkill (no need
//  for pod-level networking, custom CRDs, or 50+ workloads). Container
//  Apps hits the sweet spot for a small portfolio API and matches the
//  job description on the resume bullet.
// ─────────────────────────────────────────────────────────────────────────────

@description('Azure region.')
param location string = resourceGroup().location

@description('Environment short name (dev, prod).')
param env string

@description('Common resource tags inherited from main.bicep.')
param tags object = {}

// TODO Phase 11: Container Apps Environment + retail-api Container App,
// scaling rules, ingress, secrets from Key Vault references.
