// ─────────────────────────────────────────────────────────────────────────────
//  storage module — placeholder for Phase 4 (product images + AI artifacts).
//
//  WHEN ACTIVATED, deploys:
//    - StorageV2 account, LRS replication (cheap; portfolio doesn't need GRS)
//    - Blob container `products` (public read for product images via SAS-less
//      direct URLs)
//    - Blob container `ml-models` (private; serves trained ML.NET pipelines
//      to the api at startup)
//    - Blob container `ai-artifacts` (private; LLM prompt logs for review)
//
//  In dev this maps to Azurite via the connection string in docker-compose;
//  in prod it's the real account with managed-identity access from the api.
// ─────────────────────────────────────────────────────────────────────────────

@description('Azure region.')
param location string = resourceGroup().location

@description('Environment short name (dev, prod).')
param env string

@description('Common resource tags inherited from main.bicep.')
param tags object = {}

// TODO Phase 4: StorageV2 LRS, public access for `products` container only,
// blob-level versioning ON, soft-delete 7d, diagnostic settings.
