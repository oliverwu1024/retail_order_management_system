// Production parameter file. Same shape as dev.bicepparam — only the
// env value (and downstream naming + tier choices) differs.

using '../main.bicep'

param env = 'prod'
param location = 'australiaeast'
