// ============================================================================
// main.dev.bicepparam — Development environment parameter file
//
// SKU choices prioritise cost over resilience:
//   SQL     : Basic (5 DTUs, 2 GB)  ~$5/month
//   Bus     : Standard              ~$10/month + messaging units
//   App Svc : B1 (1 vCPU, 1.75 GB) ~$13/month
//
// The SQL admin password is read from an environment variable so that this
// file can be committed to source control without embedding secrets.
// Before deploying, set:  $env:SQL_ADMIN_PASSWORD = 'YourDevP@ssw0rd!'
//
// Deploy:
//   az group create --name rg-busbooking-dev --location eastus
//   az deployment group create \
//     --resource-group rg-busbooking-dev \
//     --template-file main.bicep \
//     --parameters main.dev.bicepparam
// ============================================================================

using 'main.bicep'

param appName          = 'busbooking'
param location         = 'southeastasia'
param environment      = 'dev'

param sqlAdminLogin    = 'sqladmin'
param sqlAdminPassword = readEnvironmentVariable('SQL_ADMIN_PASSWORD')

// Basic (5 DTU) — cheapest paid SQL tier; enough for dev/test load.
param sqlSkuName       = 'Basic'
param sqlCapacity      = 5

// Topics require Standard or Premium tier — Basic only supports queues.
param serviceBusSku    = 'Standard'

// B1 — has Always On; avoids the 60-min/day CPU cap of the Free tier.
param appServicePlanSku = 'B1'
