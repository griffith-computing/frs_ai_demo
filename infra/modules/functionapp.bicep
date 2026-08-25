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

@description('Cosmos DB Uploads container name.')
param cosmosUploadsContainerName string

@description('Azure AI Face API endpoint.')
param faceApiEndpoint string

@description('Name of the blob container used to store uploaded photos.')
param photosContainerName string = 'photos'

@description('Name of the Face API Dynamic Person Group used for no-training identification.')
param dynamicPersonGroupId string = 'frs-ai-demo-group'

@description('Resource ID of the VNet subnet (delegated to Microsoft.Web/serverFarms) used for outbound VNet integration.')
param integrationSubnetId string

var hostingPlanName = '${functionAppName}-plan'

resource hostingPlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: hostingPlanName
  location: location
  sku: {
    // Elastic Premium: Consumption (Y1) doesn't support VNet integration, which is required
    // to reach the storage/Cosmos/Event Hub/Face API private endpoints.
    name: 'EP1'
    tier: 'ElasticPremium'
  }
  properties: {
    reserved: false
    maximumElasticWorkerCount: 20
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
    // No WEBSITE_CONTENTAZUREFILECONNECTIONSTRING/WEBSITE_CONTENTSHARE: Azure Files only
    // supports key-based auth, which this storage account (no shared key access) can't provide.
    // Deployment uses zip-deploy instead, per "Create an app without Azure Files" guidance.
    virtualNetworkSubnetId: integrationSubnetId
    vnetRouteAllEnabled: true
    siteConfig: {
      netFrameworkVersion: 'v10.0'
      use32BitWorkerProcess: false
      appSettings: [
        {
          // Route all outbound through the VNet and resolve private DNS zones (Storage, Cosmos,
          // Event Hub, Face API) via Azure DNS; without this the Face API private endpoint
          // hostname can resolve to its public IP and return 403 "not from an approved private endpoint".
          name: 'WEBSITE_VNET_ROUTE_ALL'
          value: '1'
        }
        {
          name: 'WEBSITE_DNS_SERVER'
          value: '168.63.129.16'
        }
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
          // Function/host keys are normally persisted as blobs in AzureWebJobsStorage, which
          // requires shared-key access; storage accounts that disable it (as this one does)
          // can't generate/retrieve keys unless secrets are stored on the local filesystem instead.
          name: 'AzureWebJobsSecretStorageType'
          value: 'files'
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
          name: 'CosmosDb__UploadsContainerName'
          value: cosmosUploadsContainerName
        }
        {
          name: 'FaceApi__Endpoint'
          value: faceApiEndpoint
        }
        {
          name: 'FaceApi__DynamicPersonGroupId'
          value: dynamicPersonGroupId
        }
      ]
    }
  }
}

output functionAppId string = functionApp.id
output functionAppName string = functionApp.name
output functionAppHostName string = functionApp.properties.defaultHostName
