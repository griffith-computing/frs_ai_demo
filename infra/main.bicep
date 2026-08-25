@description('Short project prefix used to derive resource names (lowercase, alphanumeric).')
@minLength(3)
@maxLength(12)
param namePrefix string = 'frsaidemo'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Face API Dynamic Person Group id used for no-training identification.')
param dynamicPersonGroupId string = 'frs-ai-demo-group'

@description('Microsoft Entra tenant ID for reviewer sign-in.')
param entraTenantId string = tenant().tenantId

@description('Client ID of the Microsoft Entra web application registration.')
param entraClientId string

@secure()
@description('Client secret of the Microsoft Entra web application registration.')
param entraClientSecret string

var suffix = uniqueString(resourceGroup().id)
var storageAccountName = toLower('${namePrefix}st${suffix}')
var eventHubNamespaceName = '${namePrefix}-ehns-${suffix}'
var cosmosAccountName = toLower('${namePrefix}-cosmos-${suffix}')
var faceAccountName = '${namePrefix}-face-${suffix}'
var functionAppName = '${namePrefix}-func-${suffix}'
var webAppName = '${namePrefix}-web-${suffix}'
var appInsightsName = '${namePrefix}-appi-${suffix}'
var identityName = '${namePrefix}-id-${suffix}'
var webIdentityName = '${namePrefix}-web-id-${suffix}'
var vnetName = '${namePrefix}-vnet-${suffix}'

module network 'modules/network.bicep' = {
  name: 'networkDeploy'
  params: {
    vnetName: vnetName
    location: location
  }
}

module identity 'modules/identity.bicep' = {
  name: 'identityDeploy'
  params: {
    identityName: identityName
    location: location
  }
}

module webIdentity 'modules/identity.bicep' = {
  name: 'webIdentityDeploy'
  params: {
    identityName: webIdentityName
    location: location
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storageDeploy'
  params: {
    storageAccountName: storageAccountName
    location: location
  }
}

module eventHub 'modules/eventhub.bicep' = {
  name: 'eventHubDeploy'
  params: {
    namespaceName: eventHubNamespaceName
    location: location
  }
}

module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmosDeploy'
  params: {
    accountName: cosmosAccountName
    location: location
  }
}

module face 'modules/face.bicep' = {
  name: 'faceDeploy'
  params: {
    faceAccountName: faceAccountName
    location: location
  }
}

module appInsights 'modules/appinsights.bicep' = {
  name: 'appInsightsDeploy'
  params: {
    appInsightsName: appInsightsName
    location: location
  }
}

module privateEndpoints 'modules/privateendpoints.bicep' = {
  name: 'privateEndpointsDeploy'
  params: {
    location: location
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    vnetId: network.outputs.vnetId
    storageAccountId: resourceId('Microsoft.Storage/storageAccounts', storageAccountName)
    storageAccountName: storageAccountName
    eventHubNamespaceId: resourceId('Microsoft.EventHub/namespaces', eventHubNamespaceName)
    eventHubNamespaceName: eventHubNamespaceName
    cosmosAccountId: resourceId('Microsoft.DocumentDB/databaseAccounts', cosmosAccountName)
    cosmosAccountName: cosmosAccountName
    faceAccountId: resourceId('Microsoft.CognitiveServices/accounts', faceAccountName)
    faceAccountName: faceAccountName
  }
  dependsOn: [
    storage
    eventHub
    cosmos
    face
  ]
}

module functionApp 'modules/functionapp.bicep' = {
  name: 'functionAppDeploy'
  params: {
    functionAppName: functionAppName
    location: location
    storageAccountName: storage.outputs.storageAccountName
    appInsightsConnectionString: appInsights.outputs.appInsightsConnectionString
    userAssignedIdentityId: identity.outputs.identityId
    userAssignedIdentityClientId: identity.outputs.identityClientId
    eventHubFullyQualifiedNamespace: eventHub.outputs.fullyQualifiedNamespace
    eventHubName: eventHub.outputs.eventHubName
    eventHubConsumerGroup: eventHub.outputs.consumerGroupName
    cosmosEndpoint: cosmos.outputs.cosmosEndpoint
    cosmosDatabaseName: cosmos.outputs.databaseName
    cosmosFacesContainerName: cosmos.outputs.facesContainerName
    cosmosUploadsContainerName: cosmos.outputs.uploadsContainerName
    faceApiEndpoint: face.outputs.faceEndpoint
    dynamicPersonGroupId: dynamicPersonGroupId
    integrationSubnetId: network.outputs.integrationSubnetId
  }
  dependsOn: [
    privateEndpoints
  ]
}

module rbac 'modules/rbac.bicep' = {
  name: 'rbacDeploy'
  params: {
    principalId: identity.outputs.identityPrincipalId
    storageAccountName: storage.outputs.storageAccountName
    eventHubNamespaceName: eventHub.outputs.namespaceName
    faceAccountName: face.outputs.faceAccountName
    cosmosAccountName: cosmos.outputs.cosmosAccountName
  }
}

module webApp 'modules/webapp.bicep' = {
  name: 'webAppDeploy'
  params: {
    webAppName: webAppName
    location: location
    userAssignedIdentityId: webIdentity.outputs.identityId
    userAssignedIdentityClientId: webIdentity.outputs.identityClientId
    integrationSubnetId: network.outputs.webIntegrationSubnetId
    appInsightsConnectionString: appInsights.outputs.appInsightsConnectionString
    logAnalyticsWorkspaceId: appInsights.outputs.logAnalyticsWorkspaceId
    entraTenantId: entraTenantId
    entraClientId: entraClientId
    entraClientSecret: entraClientSecret
    cosmosEndpoint: cosmos.outputs.cosmosEndpoint
    cosmosDatabaseName: cosmos.outputs.databaseName
    cosmosFacesContainerName: cosmos.outputs.facesContainerName
    cosmosUploadsContainerName: cosmos.outputs.uploadsContainerName
    cosmosReviewsContainerName: cosmos.outputs.reviewsContainerName
    storageAccountName: storage.outputs.storageAccountName
    eventHubFullyQualifiedNamespace: eventHub.outputs.fullyQualifiedNamespace
    eventHubName: eventHub.outputs.eventHubName
  }
  dependsOn: [
    privateEndpoints
  ]
}

module webRbac 'modules/webrbac.bicep' = {
  name: 'webRbacDeploy'
  params: {
    principalId: webIdentity.outputs.identityPrincipalId
    storageAccountName: storage.outputs.storageAccountName
    eventHubNamespaceName: eventHub.outputs.namespaceName
    eventHubName: eventHub.outputs.eventHubName
    cosmosAccountName: cosmos.outputs.cosmosAccountName
    cosmosDatabaseName: cosmos.outputs.databaseName
    facesContainerName: cosmos.outputs.facesContainerName
    uploadsContainerName: cosmos.outputs.uploadsContainerName
    reviewsContainerName: cosmos.outputs.reviewsContainerName
  }
}

output functionAppName string = functionApp.outputs.functionAppName
output functionAppHostName string = functionApp.outputs.functionAppHostName
output storageAccountName string = storage.outputs.storageAccountName
output eventHubNamespaceName string = eventHub.outputs.namespaceName
output eventHubName string = eventHub.outputs.eventHubName
output cosmosAccountName string = cosmos.outputs.cosmosAccountName
output cosmosUploadsContainerName string = cosmos.outputs.uploadsContainerName
output cosmosReviewsContainerName string = cosmos.outputs.reviewsContainerName
output faceAccountName string = face.outputs.faceAccountName
output webAppName string = webApp.outputs.webAppName
output webAppHostName string = webApp.outputs.webAppHostName
