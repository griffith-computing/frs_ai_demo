//----------------------------------------------------------------------------------
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND,
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE.
//
// This sample is not supported under any Microsoft standard support program or
// service. It is provided to you solely for the purpose of illustration and is
// intended to be modified, tested, and validated by the customer prior to any
// production use. The entire risk arising out of the use or performance of this
// code remains with the customer.
//
// Copyright (c) Microsoft Corporation. All rights reserved.
//----------------------------------------------------------------------------------

type bulkUploadSource = {
  key: string
  displayName: string
  accountName: string
  containerName: string
  allowCopy: bool
  allowInPlace: bool
  grantRbac: bool
}

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

@description('Client ID of the single-tenant Microsoft Entra application registration that represents the upload Function API.')
param uploadApiClientId string

@description('Set to true to reuse an existing storage account instead of provisioning a new one. The existing account must already exist in this resource group.')
param useExistingStorageAccount bool = false

@description('Name of the existing storage account to reuse. Required when useExistingStorageAccount is true; must be in this resource group.')
param existingStorageAccountName string = ''

@description('Set to true to reuse an existing Event Hubs namespace instead of provisioning a new one. The existing namespace must already exist in this resource group.')
param useExistingEventHubNamespace bool = false

@description('Name of the existing Event Hubs namespace to reuse. Required when useExistingEventHubNamespace is true; must be in this resource group.')
param existingEventHubNamespaceName string = ''

@description('Set to true to reuse an existing Cosmos DB account instead of provisioning a new one. The existing account must already exist in this resource group.')
param useExistingCosmosAccount bool = false

@description('Name of the existing Cosmos DB account to reuse. Required when useExistingCosmosAccount is true; must be in this resource group.')
param existingCosmosAccountName string = ''

@description('Set to true to reuse an existing Azure AI Face account instead of provisioning a new one. The existing account must already exist in this resource group.')
param useExistingFaceAccount bool = false

@description('Name of the existing Azure AI Face (Cognitive Services, kind=Face) account to reuse. Required when useExistingFaceAccount is true; must be in this resource group.')
param existingFaceAccountName string = ''

@description('Set to true to reuse an existing Function App and its App Service plan. Both resources must already exist in this resource group and match the deployment region.')
param useExistingFunctionApp bool = false

@description('Name of the existing Function App to reuse. Required when useExistingFunctionApp is true; must be in this resource group and deployment region.')
param existingFunctionAppName string = ''

@description('Name of the existing App Service plan used by the Function App. Required when useExistingFunctionApp is true; must be in this resource group and deployment region.')
param existingFunctionAppPlanName string = ''

@description('Set to true to reuse an existing Web App and its App Service plan. Both resources must already exist in this resource group and match the deployment region.')
param useExistingWebApp bool = false

@description('Name of the existing Web App to reuse. Required when useExistingWebApp is true; must be in this resource group and deployment region.')
param existingWebAppName string = ''

@description('Name of the existing App Service plan used by the Web App. Required when useExistingWebApp is true; must be in this resource group and deployment region.')
param existingWebAppPlanName string = ''

@description('Enables browser batch upload and allowlisted storage import. Disabled by default.')
param bulkUploadsEnabled bool = false

@description('Maximum number of files accepted in one browser batch.')
@minValue(1)
@maxValue(100)
param bulkUploadsMaxFiles int = 100

@description('Maximum number of concurrent browser upload requests.')
@minValue(1)
@maxValue(10)
param bulkUploadsMaxConcurrency int = 3

@description('Allowlisted storage import sources. Each object must contain key, displayName, accountName, containerName, allowCopy, allowInPlace, and grantRbac. Set grantRbac=true only when the source account and container exist in this resource group.')
param bulkUploadSources bulkUploadSource[] = []

@description('Timer schedule used to process storage imports, in NCRONTAB format.')
param bulkUploadsImportSchedule string = '0 */1 * * * *'

@description('Blob container used by the FaceLab web app for staged images.')
param faceLabImagesContainerName string = 'facelab-images'

var suffix = uniqueString(resourceGroup().id)
var storageAccountName = toLower('${namePrefix}st${suffix}')
var eventHubNamespaceName = '${namePrefix}-ehns-${suffix}'
var cosmosAccountName = toLower('${namePrefix}-cosmos-${suffix}')
var faceAccountName = '${namePrefix}-face-${suffix}'
var functionAppName = '${namePrefix}-func-${suffix}'
var webAppName = '${namePrefix}-web-${suffix}'
var faceLabWebAppName = '${namePrefix}-facelab-${suffix}'
var appInsightsName = '${namePrefix}-appi-${suffix}'
var identityName = '${namePrefix}-id-${suffix}'
var webIdentityName = '${namePrefix}-web-id-${suffix}'
var faceLabWebIdentityName = '${namePrefix}-facelab-id-${suffix}'
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

module faceLabWebIdentity 'modules/identity.bicep' = {
  name: 'faceLabWebIdentityDeploy'
  params: {
    identityName: faceLabWebIdentityName
    location: location
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storageDeploy'
  params: {
    storageAccountName: storageAccountName
    location: location
    useExistingStorageAccount: useExistingStorageAccount
    existingStorageAccountName: existingStorageAccountName
    faceLabImagesContainerName: faceLabImagesContainerName
  }
}

module eventHub 'modules/eventhub.bicep' = {
  name: 'eventHubDeploy'
  params: {
    namespaceName: eventHubNamespaceName
    location: location
    useExistingEventHubNamespace: useExistingEventHubNamespace
    existingEventHubNamespaceName: existingEventHubNamespaceName
  }
}

module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmosDeploy'
  params: {
    accountName: cosmosAccountName
    location: location
    useExistingCosmosAccount: useExistingCosmosAccount
    existingCosmosAccountName: existingCosmosAccountName
  }
}

module face 'modules/face.bicep' = {
  name: 'faceDeploy'
  params: {
    faceAccountName: faceAccountName
    location: location
    useExistingFaceAccount: useExistingFaceAccount
    existingFaceAccountName: existingFaceAccountName
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
    storageAccountId: storage.outputs.storageAccountId
    storageAccountName: storage.outputs.storageAccountName
    eventHubNamespaceId: eventHub.outputs.namespaceId
    eventHubNamespaceName: eventHub.outputs.namespaceName
    cosmosAccountId: cosmos.outputs.cosmosAccountId
    cosmosAccountName: cosmos.outputs.cosmosAccountName
    faceAccountId: face.outputs.faceAccountId
    faceAccountName: face.outputs.faceAccountName
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
    useExistingFunctionApp: useExistingFunctionApp
    existingFunctionAppName: existingFunctionAppName
    existingFunctionAppPlanName: existingFunctionAppPlanName
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
    entraTenantId: entraTenantId
    uploadApiClientId: uploadApiClientId
    bulkUploadsEnabled: bulkUploadsEnabled
    bulkUploadsMaxFiles: bulkUploadsMaxFiles
    bulkUploadsMaxConcurrency: bulkUploadsMaxConcurrency
    bulkUploadSources: bulkUploadSources
    bulkUploadsImportSchedule: bulkUploadsImportSchedule
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
    useExistingWebApp: useExistingWebApp
    existingWebAppName: existingWebAppName
    existingWebAppPlanName: existingWebAppPlanName
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
    bulkUploadsEnabled: bulkUploadsEnabled
    bulkUploadsMaxFiles: bulkUploadsMaxFiles
    bulkUploadsMaxConcurrency: bulkUploadsMaxConcurrency
    bulkUploadSources: bulkUploadSources
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

module faceLabWebApp 'modules/facelabwebapp.bicep' = {
  name: 'faceLabWebAppDeploy'
  params: {
    webAppName: faceLabWebAppName
    location: location
    userAssignedIdentityId: faceLabWebIdentity.outputs.identityId
    userAssignedIdentityClientId: faceLabWebIdentity.outputs.identityClientId
    integrationSubnetId: network.outputs.webIntegrationSubnetId
    appInsightsConnectionString: appInsights.outputs.appInsightsConnectionString
    logAnalyticsWorkspaceId: appInsights.outputs.logAnalyticsWorkspaceId
    storageAccountName: storage.outputs.storageAccountName
    faceLabImagesContainerName: storage.outputs.faceLabImagesContainerName
    faceApiEndpoint: face.outputs.faceEndpoint
    dynamicPersonGroupId: dynamicPersonGroupId
  }
  dependsOn: [
    privateEndpoints
  ]
}

module faceLabRbac 'modules/rbac.bicep' = {
  name: 'faceLabRbacDeploy'
  params: {
    principalId: faceLabWebIdentity.outputs.identityPrincipalId
    storageAccountName: storage.outputs.storageAccountName
    eventHubNamespaceName: eventHub.outputs.namespaceName
    faceAccountName: face.outputs.faceAccountName
    cosmosAccountName: cosmos.outputs.cosmosAccountName
  }
}

module bulkUploadSourceRbac 'modules/bulkuploadrbac.bicep' = [for source in bulkUploadSources: if (source.grantRbac) {
  name: 'bulkUploadSourceRbac-${uniqueString(source.accountName, source.containerName)}'
  params: {
    storageAccountName: source.accountName
    containerName: source.containerName
    webPrincipalId: webIdentity.outputs.identityPrincipalId
    functionPrincipalId: identity.outputs.identityPrincipalId
    allowInPlace: source.allowInPlace
  }
}]

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
output faceLabWebAppName string = faceLabWebApp.outputs.webAppName
output faceLabWebAppHostName string = faceLabWebApp.outputs.webAppHostName
output faceLabImagesContainerName string = storage.outputs.faceLabImagesContainerName
