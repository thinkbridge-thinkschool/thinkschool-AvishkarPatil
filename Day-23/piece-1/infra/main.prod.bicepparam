// ============================================================================
// main.prod.bicepparam — Production environment parameter file
//
// SKU choices favour resilience and throughput over cost:
//   SQL     : S2 (50 DTUs, 250 GB)  ~$75/month
//   Bus     : Standard              (same tier; Premium adds Geo-DR if needed)
//   App Svc : P1v3 (2 vCPU, 8 GB)  ~$138/month — supports zone redundancy
//
// The SQL admin password is read from an environment variable.
// In CI/CD (GitHub Actions), set SQL_ADMIN_PASSWORD as a repository secret
// and expose it with:  env: { SQL_ADMIN_PASSWORD: ${{ secrets.SQL_ADMIN_PASSWORD }} }
//
// Deploy (idempotent — safe to re-run):
//   az group create --name rg-busbooking-prod --location eastus
//   az deployment group create \
//     --resource-group rg-busbooking-prod \
//     --template-file main.bicep \
//     --parameters main.prod.bicepparam
// ============================================================================

using 'main.bicep'

param appName          = 'busbooking'
param location         = 'southeastasia'
param environment      = 'prod'

param sqlAdminLogin    = 'sqladmin'
param sqlAdminPassword = readEnvironmentVariable('SQL_ADMIN_PASSWORD')

// S2 (50 DTU) — handles sustained production query load; 32 GB storage.
// Geo-redundant backup storage is enabled by sql.bicep when environment='prod'.
param sqlSkuName       = 'S2'
param sqlCapacity      = 50

param serviceBusSku    = 'Standard'

// P1v3 — Premium v3 enables zone redundancy via api.bicep's zoneRedundant flag.
// Provides 2 vCPU, 8 GB RAM; autoscale rules can be added as a follow-up.
param appServicePlanSku = 'P1v3'
