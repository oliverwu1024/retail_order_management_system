// ─────────────────────────────────────────────────────────────────────────────
//  sql module — placeholder for Phase 1 (identity + first domain entities).
//
//  WHEN ACTIVATED, deploys:
//    - Azure SQL Server (logical server) with AAD-only authentication
//    - Azure SQL Database — Serverless tier GP_S_Gen5_1
//        - 1 vCore min, auto-pause after 60 min idle
//        - Cost-optimal for a portfolio project; pays per second of compute
//    - Firewall rule allowing Azure services + the GH OIDC runner IP
//    - Diagnostic settings → monitoring module's Log Analytics workspace
//
//  WHY SERVERLESS AND NOT BASIC OR ELASTIC POOL?
//  ---------------------------------------------
//  Basic doesn't support compatibility level 160 (SQL Server 2022 features
//  we use). Elastic pool requires multiple databases to be cost-effective.
//  Serverless auto-pause means the bill drops to storage-only when no
//  traffic — perfect for a portfolio project that sees real demos only
//  occasionally.
// ─────────────────────────────────────────────────────────────────────────────

@description('Azure region.')
param location string = resourceGroup().location

@description('Environment short name (dev, prod).')
param env string

@description('Common resource tags inherited from main.bicep.')
param tags object = {}

// TODO Phase 1: SQL Server + Serverless DB, AAD admin = the GH OIDC SP,
// transparent data encryption ON (default), diagnostic settings to Log
// Analytics.
