// ─────────────────────────────────────────────────────────────────────────────
//  functions module — placeholder for Phase 8 (event consumers + scheduled
//  jobs).
//
//  WHEN ACTIVATED, deploys:
//    - Function App `retail-fn-events` on Consumption plan
//        - .NET 10 isolated worker
//        - Triggers wired to Service Bus queues, Event Grid topics, and
//          timer for scheduled jobs (loyalty expiry, tier recalc)
//        - Managed identity for Key Vault + Service Bus access
//    - Application Insights connection to the monitoring module
//
//  WHY CONSUMPTION AND NOT PREMIUM PLAN?
//  -------------------------------------
//  Consumption gives us scale-to-zero (zero cost when idle) and pays per
//  execution. Premium offers VNet integration + always-warm instances,
//  neither of which we need at portfolio scale. Cold-start tolerance is
//  fine — order confirmation is async, no human waits on it.
// ─────────────────────────────────────────────────────────────────────────────

@description('Azure region.')
param location string = resourceGroup().location

@description('Environment short name (dev, prod).')
param env string

@description('Common resource tags inherited from main.bicep.')
param tags object = {}

// TODO Phase 8: Function App Consumption plan + Storage account for runtime
// state + diagnostic settings + RBAC role assignments for Service Bus +
// Event Grid + Key Vault.
