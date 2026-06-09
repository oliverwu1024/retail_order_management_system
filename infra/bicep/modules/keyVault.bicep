// ─────────────────────────────────────────────────────────────────────────────
//  keyVault module — placeholder for Phase 1 (auth secrets).
//
//  WHEN ACTIVATED, deploys:
//    - Azure Key Vault (RBAC-only data plane, NOT access policies)
//    - Secrets: JWT signing key, SQL admin password, Stripe webhook signing
//      secret, Anthropic API key
//    - DefaultAzureCredential reads them in via managed identity (no secrets
//      ever in env vars in prod)
//
//  WHY RBAC AUTHORIZATION INSTEAD OF VAULT ACCESS POLICIES?
//  -------------------------------------------------------
//  Access policies are deprecated. RBAC uses Azure AD groups + role
//  assignments that are auditable end-to-end. Required for any modern
//  enterprise posture.
// ─────────────────────────────────────────────────────────────────────────────

@description('Azure region.')
param location string = resourceGroup().location

@description('Environment short name (dev, prod).')
param env string

@description('Common resource tags inherited from main.bicep.')
param tags object = {}

// TODO Phase 1: Key Vault with enableRbacAuthorization=true, soft-delete +
// purge protection ON, network ACL restricting to Container Apps + GH OIDC.
