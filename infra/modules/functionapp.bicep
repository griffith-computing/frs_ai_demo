@description('Name of the Function App.')
param functionAppName string

@description('Azure region for the Function App and related resources.')
param location string

@description('Storage account name used for the Function App runtime (AzureWebJobsStorage) and photo blobs.')
param storageAccountName string

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('Resource ID of the user-assigned managed identity to attach to the Function App.')
param userAssignedIdentityId string

@description('Client ID of the user-assigned managed identity (used for DefaultAzureCredential app settings).')
param userAssignedIdentityClientId string

@description('Event Hub fully qualified namespace, e.g. myns.servicebus.windows.net.')
param eventHubFullyQualifiedNamespace string

@description('Name of the Event Hub used for photo-upload events.')
param eventHubName string

@description('Name of the consumer group used by the processing function.')
param eventHubConsumerGroup string

@description('Cosmos DB endpoint URI.')
param cosmosEndpoint string

@description('Cosmos DB database name.')
param cosmosDatabaseName string

@description('Cosmos DB Faces container name.')
param cosmosFacesContainerName string

@description('Azure AI Face API endpoint.')
param faceApiEndpoint string

@description('Name of the blob container used to store uploaded photos.')
param photosContainerName string = 'photos'

@description('Name of the Face API PersonGroup used for identify/training.')
param personGroupId string = 'frs-ai-demo-group'

var hostingPlanName = '${functionAppName}-plan'

resource hostingPlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: hostingPlanName
  location: location
  sku: {
    name: 'Y1'
    tier: 'Dynamic'
  }
  properties: {
    reserved: false
  }
}

resource functionApp 'Microsoft.Web/sites@2023-01-01' = {
  name: functionAppName
  location: location
  kind: 'functionapp'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentityId}': {}
    }
  }
  properties: {
    serverFarmId: hostingPlan.id
    httpsOnly: true
    siteConfig: {
      netFrameworkVersion: 'v8.0'
      use32BitWorkerProcess: false
      appSettings: [
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storageAccountName
        }
        {
          name: 'AzureWebJobsStorage__credential'
          value: 'managedidentity'
        }
        {
          name: 'AzureWebJobsStorage__clientId'
          value: userAssignedIdentityClientId
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          name: 'AZURE_CLIENT_ID'
          value: userAssignedIdentityClientId
        }
        {
          name: 'PhotosStorageAccountName'
          value: storageAccountName
        }
        {
          name: 'PhotosContainerName'
          value: photosContainerName
        }
        {
          name: 'EventHub__fullyQualifiedNamespace'
          value: eventHubFullyQualifiedNamespace
        }
        {
          name: 'EventHubName'
          value: eventHubName
        }
        {
          name: 'EventHubConsumerGroup'
          value: eventHubConsumerGroup
        }
        {
          name: 'CosmosDb__Endpoint'
          value: cosmosEndpoint
        }
        {
          name: 'CosmosDb__DatabaseName'
          value: cosmosDatabaseName
        }
        {
          name: 'CosmosDb__FacesContainerName'
          value: cosmosFacesContainerName
        }
        {
          name: 'FaceApi__Endpoint'
          value: faceApiEndpoint
        }
        {
          name: 'FaceApi__PersonGroupId'
          value: personGroupId
        }
      ]
    }
  }
}

output functionAppId string = functionApp.id
output functionAppName string = functionApp.name
output functionAppHostName string = functionApp.properties.defaultHostName
