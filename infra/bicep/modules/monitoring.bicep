// ─────────────────────────────────────────────────────────────────────────────
//  monitoring module — placeholder for Phase 9 (observability + runbooks).
//
//  WHEN ACTIVATED, deploys:
//    - Log Analytics workspace (the single observability backplane)
//    - Application Insights (workspace-based, so it forwards to Log Analytics)
//    - Diagnostic Settings hookups for every other module's resources
//
//  This module ships FIRST in main.bicep's activation order because every
//  downstream module wires its diagnostics into this workspace.
// ─────────────────────────────────────────────────────────────────────────────

@description('Azure region.')
param location string = resourceGroup().location

@description('Environment short name (dev, prod).')
param env string

@description('Common resource tags inherited from main.bicep.')
param tags object = {}

// TODO Phase 9: Log Analytics workspace, Application Insights, diagnostic
// settings for every other resource.
