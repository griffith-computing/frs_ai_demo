@description('Name of the reviewer web app.')
param webAppName string

@description('Azure region for the web app.')
param location string

@description('Resource ID of the user-assigned identity attached to the web app.')
param userAssignedIdentityId string

@description('Client ID of the user-assigned identity.')
param userAssignedIdentityClientId string

@description('Resource ID of the dedicated App Service VNet integration subnet.')
param integrationSubnetId string

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('Log Analytics workspace resource ID for App Service diagnostics.')
param logAnalyticsWorkspaceId string

@description('Microsoft Entra tenant ID for single-tenant sign-in.')
param entraTenantId string

@description('Microsoft Entra application client ID.')
param entraClientId string

@secure()
@description('Microsoft Entra application client secret. Supply at deployment time.')
param entraClientSecret string

@description('Cosmos DB endpoint URI.')
param cosmosEndpoint string

@description('Cosmos database name.')
param cosmosDatabaseName string

@description('Cosmos Faces container name.')
param cosmosFacesContainerName string

@description('Cosmos Uploads container name.')
param cosmosUploadsContainerName string

@description('Cosmos Reviews container name.')
param cosmosReviewsContainerName string

@description('Storage account containing source photos.')
param storageAccountName string

@description('Event Hub namespace host name.')
param eventHubFullyQualifiedNamespace string

@description('Event Hub name.')
param eventHubName string

var planName = '${webAppName}-plan'

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  kind: 'linux'
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  properties: {
    reserved: true
  }
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentityId}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    virtualNetworkSubnetId: integrationSubnetId
    vnetRouteAllEnabled: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health'
      cors: {
        allowedOrigins: [
          'https://${webAppName}.azurewebsites.net'
        ]
        supportCredentials: false
      }
      appSettings: [
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        { name: 'AZURE_CLIENT_ID', value: userAssignedIdentityClientId }
        { name: 'AzureAd__Instance', value: environment().authentication.loginEndpoint }
        { name: 'AzureAd__TenantId', value: entraTenantId }
        { name: 'AzureAd__ClientId', value: entraClientId }
        { name: 'AzureAd__ClientSecret', value: entraClientSecret }
        { name: 'AzureAd__CallbackPath', value: '/signin-oidc' }
        { name: 'CosmosDb__Endpoint', value: cosmosEndpoint }
        { name: 'CosmosDb__DatabaseName', value: cosmosDatabaseName }
        { name: 'CosmosDb__FacesContainerName', value: cosmosFacesContainerName }
        { name: 'CosmosDb__UploadsContainerName', value: cosmosUploadsContainerName }
        { name: 'CosmosDb__ReviewsContainerName', value: cosmosReviewsContainerName }
        { name: 'PhotosStorageAccountName', value: storageAccountName }
        { name: 'PhotosContainerName', value: 'photos' }
        { name: 'EventHub__FullyQualifiedNamespace', value: eventHubFullyQualifiedNamespace }
        { name: 'EventHub__Name', value: eventHubName }
        { name: 'Uploads__MaxBytes', value: '6291456' }
      ]
    }
  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: '${webAppName}-diagnostics'
  scope: webApp
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

output webAppId string = webApp.id
output webAppName string = webApp.name
output webAppHostName string = webApp.properties.defaultHostName
