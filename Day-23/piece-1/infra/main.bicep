// ============================================================================
// main.bicep — BusBooking IaC orchestrator
//
// Composes three child modules into a complete environment:
//   sql        → Azure SQL Server + Database
//   serviceBus → Service Bus Namespace + topics + auth rule
//   api        → Log Analytics + App Insights + App Service Plan + Web App
//
// Deploy:
//   az deployment group create \
//     --resource-group rg-busbooking-dev \
//     --parameters main.dev.bicepparam
//
// What-if (dry run):
//   az deployment group what-if \
//     --resource-group rg-busbooking-dev \
//     --parameters main.dev.bicepparam
// ============================================================================

targetScope = 'resourceGroup'

// ── Parameters ────────────────────────────────────────────────────────────────

@description('Short application name — drives all resource names (e.g. "busbooking"). Max 20 chars.')
@maxLength(20)
param appName string

@description('Azure region. Defaults to the resource group region.')
param location string = resourceGroup().location

@description('Deployment environment. Controls naming, SKUs, and firewall rules.')
@allowed(['dev', 'prod'])
param environment string

@description('SQL Server administrator login name.')
@minLength(1)
param sqlAdminLogin string

@secure()
@description('SQL Server administrator password (≥12 chars, upper+lower+digit+special).')
@minLength(12)
param sqlAdminPassword string

@description('Azure SQL Database SKU. Basic suits dev; S2 suits prod.')
@allowed(['Basic', 'S1', 'S2', 'S3'])
param sqlSkuName string = 'Basic'

@description('SQL Database DTU capacity. Basic=5, S1=20, S2=50, S3=100.')
param sqlCapacity int = 5

@description('Service Bus tier. Standard is the minimum required for topics.')
@allowed(['Standard', 'Premium'])
param serviceBusSku string = 'Standard'

@description('App Service Plan SKU. B1 suits dev; P1v3 suits prod.')
@allowed(['B1', 'B2', 'P1v3', 'P2v3'])
param appServicePlanSku string = 'B1'

// ── Modules ───────────────────────────────────────────────────────────────────

module sql 'modules/sql.bicep' = {
  name: 'deploy-sql-${environment}'
  params: {
    appName: appName
    location: location
    environment: environment
    adminLogin: sqlAdminLogin
    adminPassword: sqlAdminPassword
    skuName: sqlSkuName
    capacity: sqlCapacity
  }
}

module serviceBus 'modules/servicebus.bicep' = {
  name: 'deploy-servicebus-${environment}'
  params: {
    appName: appName
    location: location
    environment: environment
    sku: serviceBusSku
  }
}

// api module depends implicitly on sql and serviceBus via their outputs.
module api 'modules/api.bicep' = {
  name: 'deploy-api-${environment}'
  params: {
    appName: appName
    location: location
    environment: environment
    sqlConnectionString: sql.outputs.connectionString
    serviceBusConnectionString: serviceBus.outputs.connectionString
    appServicePlanSku: appServicePlanSku
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

@description('Full HTTPS URL of the deployed API.')
output apiUrl string = 'https://${api.outputs.hostName}'

@description('App Service hostname (for adding to custom domain / cert).')
output apiHostName string = api.outputs.hostName

@description('SQL Server fully-qualified domain name.')
output sqlServerFqdn string = sql.outputs.serverFqdn

@description('SQL Database name.')
output sqlDatabaseName string = sql.outputs.databaseName
