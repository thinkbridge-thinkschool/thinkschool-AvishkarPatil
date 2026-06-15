// ============================================================================
// Module: api.bicep
// Provisions: Log Analytics Workspace → Application Insights
//             → App Service Plan → Linux Web App (.NET 10)
//
// Connection strings are injected as app settings using the ASP.NET Core
// double-underscore environment-variable convention:
//   ConnectionStrings__DefaultConnection  (maps to appsettings.json section)
//   ConnectionStrings__ServiceBus
// ============================================================================

@description('Short application name used for resource naming.')
param appName string

@description('Azure region for all resources.')
param location string

@description('Deployment environment.')
@allowed(['dev', 'prod'])
param environment string

@secure()
@description('SQL ADO.NET connection string from the sql module output.')
param sqlConnectionString string

@secure()
@description('Service Bus connection string from the servicebus module output.')
param serviceBusConnectionString string

@description('App Service Plan SKU.')
@allowed(['B1', 'B2', 'P1v3', 'P2v3'])
param appServicePlanSku string = 'B1'

// ── Derived names ─────────────────────────────────────────────────────────────
var suffix   = take(uniqueString(resourceGroup().id), 6)
var planName = 'plan-${appName}-${environment}'
// App Service names must be globally unique; suffix disambiguates.
var siteName = 'app-${appName}-${environment}-${suffix}'
var lawName  = 'law-${appName}-${environment}'
var aiName   = 'ai-${appName}-${environment}'

// Map our 'dev'/'prod' label to the ASP.NET Core environment name.
var aspNetEnv = environment == 'prod' ? 'Production' : 'Development'

// P-series plans support availability zones; B-series do not.
var isPremium = startsWith(appServicePlanSku, 'P')

// ── Log Analytics Workspace ───────────────────────────────────────────────────

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: lawName
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: environment == 'prod' ? 90 : 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
  tags: {
    environment: environment
    app: appName
  }
}

// ── Application Insights ──────────────────────────────────────────────────────

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: aiName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    RetentionInDays: environment == 'prod' ? 90 : 30
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
  tags: {
    environment: environment
    app: appName
  }
}

// ── App Service Plan ──────────────────────────────────────────────────────────

resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: planName
  location: location
  kind: 'linux'
  sku: {
    name: appServicePlanSku
  }
  properties: {
    reserved: true   // required for Linux
    zoneRedundant: isPremium && environment == 'prod'
  }
  tags: {
    environment: environment
    app: appName
  }
}

// ── Web App ───────────────────────────────────────────────────────────────────

resource webApp 'Microsoft.Web/sites@2023-01-01' = {
  name: siteName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      // alwaysOn requires B1+ — not available on F1 (Free).
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      appSettings: [
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: aspNetEnv
        }
        {
          // EF Core SQL Server connection string.
          // Double underscore = JSON path separator in .NET env-var config.
          name: 'ConnectionStrings__DefaultConnection'
          value: sqlConnectionString
        }
        {
          // Azure Service Bus connection string.
          name: 'ConnectionStrings__ServiceBus'
          value: serviceBusConnectionString
        }
        {
          // Application Insights SDK / auto-instrumentation agent picks this up.
          // Wire up in Program.cs: builder.Services.AddApplicationInsightsTelemetry()
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
          value: '~3'
        }
      ]
    }
  }
  tags: {
    environment: environment
    app: appName
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

@description('Default hostname of the Web App (no https:// prefix).')
output hostName string = webApp.properties.defaultHostName

@description('Resource ID of the Web App (needed for deployment slots, RBAC, etc.).')
output webAppId string = webApp.id

@description('Application Insights instrumentation key (legacy; prefer connection string).')
output appInsightsInstrumentationKey string = appInsights.properties.InstrumentationKey
