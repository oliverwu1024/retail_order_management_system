// ─────────────────────────────────────────────────────────────────────────────
//  registry module — placeholder for Phase 0 setup / Phase 11 deploy.
//
//  WHEN ACTIVATED, deploys:
//    - Azure Container Registry (Standard SKU; Basic doesn't support
//      managed-identity push from GitHub Actions)
//    - AcrPush role assignment for the GitHub OIDC identity
//    - AcrPull role assignment for the Container Apps managed identity
//
//  PRIVATE? Not at this scale — public ACR + AAD auth is fine. Promote to
//  Private Link only if we onboard private-network workloads that mustn't
//  see the public endpoint.
// ─────────────────────────────────────────────────────────────────────────────

@description('Azure region.')
param location string = resourceGroup().location

@description('Environment short name (dev, prod).')
param env string

@description('Common resource tags inherited from main.bicep.')
param tags object = {}

// TODO Phase 0/11: ACR Standard, anonymous pull disabled, soft-delete
// retention 7 days, AcrPush role assignment to GH OIDC SP.
