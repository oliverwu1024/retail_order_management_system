// ─────────────────────────────────────────────────────────────────────────────
//  storage module — placeholder for Phase 4 (product images + AI artifacts).
//
//  WHEN ACTIVATED, deploys:
//    - StorageV2 account, LRS replication (cheap; portfolio doesn't need GRS),
//      allowBlobPublicAccess = false (no anonymous public containers — Azure's
//      secure default)
//    - Blob container `products` (PRIVATE; product images served via short-lived
//      user-delegation SAS minted by the api, or a CDN with a private origin)
//    - Blob container `ml-models` (private; serves trained ML.NET pipelines
//      to the api at startup)
//    - Blob container `ai-artifacts` (private; LLM prompt logs for review)
//
//  In dev this maps to Azurite via the connection string in docker-compose; the
//  local-dev container is public-read (Storage:PublicReadImages) for convenience.
//  In prod it's the real account with managed-identity access from the api — all
//  containers private, no anonymous public access.
// ─────────────────────────────────────────────────────────────────────────────

@description('Azure region.')
param location string = resourceGroup().location

@description('Environment short name (dev, prod).')
param env string

@description('Common resource tags inherited from main.bicep.')
param tags object = {}

// TODO Phase 4: StorageV2 LRS, allowBlobPublicAccess = false (all containers
// private; product images via user-delegation SAS / CDN), blob-level versioning ON,
// soft-delete 7d, diagnostic settings.
