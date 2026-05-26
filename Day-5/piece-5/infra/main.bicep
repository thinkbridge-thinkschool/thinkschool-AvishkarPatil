targetScope = 'subscription'

@minLength(1)
@maxLength(64)
@description('Name of the azd environment. Used to seed resource names and tags.')
param environmentName string

@minLength(1)
@description('Azure region for the new resources (ACR, identity, Container App).')
param location string

@description('Set to true on subsequent deploys when the api image already exists in ACR.')
param apiExists bool = false

@secure()
@description('Definition of the api service, injected by azd between provision and deploy.')
param apiDefinition object = {}

@description('Resource group that hosts the existing Container Apps environment and will also host the new ACR + Container App.')
param resourceGroupName string = 'thinkschool-rg'

@description('Name of the existing Container Apps environment to deploy the api into.')
param containerAppsEnvironmentName string = 'thinkschool-env'

var tags = {
  'azd-env-name': environmentName
}

var resourceToken = uniqueString(subscription().id, environmentName, location)

resource resourceGroup 'Microsoft.Resources/resourceGroups@2024-03-01' existing = {
  name: resourceGroupName
}

module resources 'resources.bicep' = {
  scope: resourceGroup
  name: 'resources'
  params: {
    location: location
    tags: tags
    resourceToken: resourceToken
    apiExists: apiExists
    apiDefinition: apiDefinition
    containerAppsEnvironmentName: containerAppsEnvironmentName
  }
}

output AZURE_LOCATION string = location
output AZURE_TENANT_ID string = tenant().tenantId
output AZURE_RESOURCE_GROUP string = resourceGroup.name
output AZURE_CONTAINER_REGISTRY_ENDPOINT string = resources.outputs.AZURE_CONTAINER_REGISTRY_ENDPOINT
output AZURE_CONTAINER_REGISTRY_NAME string = resources.outputs.AZURE_CONTAINER_REGISTRY_NAME
output AZURE_CONTAINER_APPS_ENVIRONMENT_NAME string = containerAppsEnvironmentName
output SERVICE_API_NAME string = resources.outputs.SERVICE_API_NAME
output SERVICE_API_URI string = resources.outputs.SERVICE_API_URI
