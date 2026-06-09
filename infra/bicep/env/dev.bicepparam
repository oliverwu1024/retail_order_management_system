// Per-environment parameter file. Replaces the legacy parameters.json shape
// — bicepparam files are statically type-checked against main.bicep at
// build time, so a renamed param fails the compile instead of failing the
// deployment.
//
// Used by:
//   az deployment sub create \
//     --location australiaeast \
//     --template-file main.bicep \
//     --parameters env/dev.bicepparam

using '../main.bicep'

param env = 'dev'
param location = 'australiaeast'
