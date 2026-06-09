// ─────────────────────────────────────────────────────────────────────────────
//  Retail OMS — top-level Bicep orchestrator.
//
//  Target scope: subscription. This template creates the resource group and
//  composes the 12 per-service modules. Each module is a placeholder today
//  (no resources declared); they activate as the phases that need them land.
//
//  WHY targetScope = 'subscription' INSTEAD OF 'resourceGroup'?
//  -----------------------------------------------------------
//  Because we want THIS template to create the RG itself. Subscription-scope
//  deployments can include child RG-scope modules (via `scope: rg`), but the
//  reverse is not true. Keeping the RG in code means it's never a manual
//  click in the portal that drifts from environment to environment.
//
//  WHY ALL THE MODULES ARE COMMENTED OUT
//  -------------------------------------
//  Empty modules compile fine but a `module x ... { ... }` invocation
//  requires the module's params to be supplied. Commenting out the
//  invocations means main.bicep compiles today, ci.yml passes today, and
//  we activate each module one PR at a time as its resources land.
// ─────────────────────────────────────────────────────────────────────────────

targetScope = 'subscription'

@description('Short environment name. Drives naming and tier selection.')
@allowed([
  'dev'
  'prod'
])
param env string

@description('Azure region for the resource group and all resources.')
param location string = 'australiaeast'

@description('Tags applied to every resource. Override at the bicepparam level if needed.')
param tags object = {
  project: 'Retail OMS'
  env: env
  managedBy: 'Bicep'
}

// Resource group name follows CAF: rg-<workload>-<env>.
// Single RG per environment is fine at this scale; promote to multiple RGs
// if/when blast-radius isolation needs it (e.g., separating data plane).
var rgName = 'rg-retail-${env}'

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: rgName
  location: location
  tags: tags
}

// ─── Module activations (uncomment as each phase ships) ─────────────────────
//
// Order below is the activation sequence — earlier modules are dependencies
// of later ones. monitoring goes first because everything else attaches its
// diagnostic settings to that workspace.
//
// module monitoring 'modules/monitoring.bicep' = {
//   scope: rg
//   name: 'monitoring'
//   params: { location: location, env: env, tags: tags }
// }
//
// module keyVault 'modules/keyVault.bicep' = {
//   scope: rg
//   name: 'keyVault'
//   params: { location: location, env: env, tags: tags }
// }
//
// module registry 'modules/registry.bicep' = {
//   scope: rg
//   name: 'registry'
//   params: { location: location, env: env, tags: tags }
// }
//
// module sql 'modules/sql.bicep' = {
//   scope: rg
//   name: 'sql'
//   params: { location: location, env: env, tags: tags }
// }
//
// module storage 'modules/storage.bicep' = {
//   scope: rg
//   name: 'storage'
//   params: { location: location, env: env, tags: tags }
// }
//
// module ai 'modules/ai.bicep' = {
//   scope: rg
//   name: 'ai'
//   params: { location: location, env: env, tags: tags }
// }
//
// module containerApps 'modules/containerApps.bicep' = {
//   scope: rg
//   name: 'containerApps'
//   params: { location: location, env: env, tags: tags }
// }
//
// module staticWebApp 'modules/staticWebApp.bicep' = {
//   scope: rg
//   name: 'staticWebApp'
//   params: { location: location, env: env, tags: tags }
// }
//
// module serviceBus 'modules/serviceBus.bicep' = {
//   scope: rg
//   name: 'serviceBus'
//   params: { location: location, env: env, tags: tags }
// }
//
// module eventGrid 'modules/eventGrid.bicep' = {
//   scope: rg
//   name: 'eventGrid'
//   params: { location: location, env: env, tags: tags }
// }
//
// module functions 'modules/functions.bicep' = {
//   scope: rg
//   name: 'functions'
//   params: { location: location, env: env, tags: tags }
// }
//
// module apim 'modules/apim.bicep' = {
//   scope: rg
//   name: 'apim'
//   params: { location: location, env: env, tags: tags }
// }

output resourceGroupName string = rg.name
output resourceGroupId string = rg.id
