// ============================================================================
// Module: sql.bicep
// Provisions: SQL Server + SQL Database + firewall rules
// ============================================================================

@description('Short application name used for resource naming.')
param appName string

@description('Azure region for all resources.')
param location string

@description('Deployment environment — drives naming and tier defaults.')
@allowed(['dev', 'prod'])
param environment string

@description('SQL Server administrator login name.')
param adminLogin string

@secure()
@description('SQL Server administrator password (≥12 chars, upper+lower+digit+special).')
param adminPassword string

@description('Azure SQL Database SKU name.')
@allowed(['Basic', 'S1', 'S2', 'S3'])
param skuName string = 'Basic'

@description('DTU capacity. Basic = 5, S1 = 20, S2 = 50, S3 = 100.')
param capacity int = 5

// ── Derived names ─────────────────────────────────────────────────────────────
// uniqueString gives a deterministic 13-char hash of the resource group; we take
// 6 chars so the full SQL Server name stays well within the 63-char limit.
var suffix     = take(uniqueString(resourceGroup().id), 6)
var serverName = 'sql-${appName}-${environment}-${suffix}'
var dbName     = 'sqldb-${appName}-${environment}'

// Derive the DTU tier from the SKU name.
var skuTier = skuName == 'Basic' ? 'Basic' : 'Standard'

// ── SQL Server ────────────────────────────────────────────────────────────────

resource sqlServer 'Microsoft.Sql/servers@2022-11-01-preview' = {
  name: serverName
  location: location
  properties: {
    administratorLogin: adminLogin
    administratorLoginPassword: adminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
  tags: {
    environment: environment
    app: appName
  }
}

// Allow outbound connections from all Azure-hosted services (0.0.0.0 → 0.0.0.0
// is the Azure "Allow Azure services" sentinel, not a real CIDR range).
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2022-11-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Dev only: open firewall for developer workstations.
// This rule is deliberately absent in prod — production traffic flows
// exclusively through the App Service, which is an Azure service above.
resource allowDevAccess 'Microsoft.Sql/servers/firewallRules@2022-11-01-preview' = if (environment == 'dev') {
  parent: sqlServer
  name: 'AllowDevClientAccess'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '255.255.255.255'
  }
}

// ── Database ──────────────────────────────────────────────────────────────────

resource sqlDatabase 'Microsoft.Sql/servers/databases@2022-11-01-preview' = {
  parent: sqlServer
  name: dbName
  location: location
  sku: {
    name: skuName
    tier: skuTier
    capacity: capacity
  }
  properties: {
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    // 2 GB for Basic, 32 GB for Standard tiers.
    maxSizeBytes: skuName == 'Basic' ? 2147483648 : 34359738368
    zoneRedundant: false
    readScale: 'Disabled'
    requestedBackupStorageRedundancy: environment == 'prod' ? 'Geo' : 'Local'
  }
  tags: {
    environment: environment
    app: appName
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

@description('Fully-qualified domain name of the SQL Server.')
output serverFqdn string = sqlServer.properties.fullyQualifiedDomainName

@description('Name of the created database.')
output databaseName string = sqlDatabase.name

@secure()
@description('ADO.NET connection string ready for use in App Service app settings.')
output connectionString string = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDatabase.name};Persist Security Info=False;User ID=${adminLogin};Password=${adminPassword};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'
