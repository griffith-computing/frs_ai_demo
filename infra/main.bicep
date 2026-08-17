@description('Short project prefix used to derive resource names (lowercase, alphanumeric).')
@minLength(3)
@maxLength(12)
param namePrefix string = 'frsaidemo'

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Face API PersonGroup id used for identify/training.')
param personGroupId string = 'frs-ai-demo-group'

var suffix = uniqueString(resourceGroup().id)
var storageAccountName = toLower('${namePrefix}st${suffix}')
var eventHubNamespaceName = '${namePrefix}-ehns-${suffix}'
var cosmosAccountName = toLower('${namePrefix}-cosmos-${suffix}')
var faceAccountName = '${namePrefix}-face-${suffix}'
var functionAppName = '${namePrefix}-func-${suffix}'
var appInsightsName = '${namePrefix}-appi-${suffix}'
var identityName = '${namePrefix}-id-${suffix}'

module identity 'modules/identity.bicep' = {
  name: 'identityDeploy'
  params: {
    identityName: identityName
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
    faceApiEndpoint: face.outputs.faceEndpoint
    personGroupId: personGroupId
  }
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

output functionAppName string = functionApp.outputs.functionAppName
output functionAppHostName string = functionApp.outputs.functionAppHostName
output storageAccountName string = storage.outputs.storageAccountName
output eventHubNamespaceName string = eventHub.outputs.namespaceName
output eventHubName string = eventHub.outputs.eventHubName
output cosmosAccountName string = cosmos.outputs.cosmosAccountName
output faceAccountName string = face.outputs.faceAccountName
