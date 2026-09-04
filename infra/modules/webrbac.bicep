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

@description('Principal ID of the reviewer web app managed identity.')
param principalId string

@description('Storage account name.')
param storageAccountName string

@description('Photos blob container name.')
param photosContainerName string = 'photos'

@description('Event Hub namespace name.')
param eventHubNamespaceName string

@description('Event Hub name.')
param eventHubName string

@description('Cosmos account name.')
param cosmosAccountName string

@description('Cosmos database name.')
param cosmosDatabaseName string

@description('Cosmos Faces container name.')
param facesContainerName string

@description('Cosmos Uploads container name.')
param uploadsContainerName string

@description('Cosmos Reviews container name.')
param reviewsContainerName string

var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var eventHubDataSenderRoleId = '2b629674-e913-4c01-ae53-ef4638d8f975'
var cosmosDataReaderRoleId = '00000000-0000-0000-0000-000000000001'
var cosmosDataContributorRoleId = '00000000-0000-0000-0000-000000000002'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' existing = {
  parent: storageAccount
  name: 'default'
}

resource photosContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' existing = {
  parent: blobService
  name: photosContainerName
}

resource eventHubNamespace 'Microsoft.EventHub/namespaces@2023-01-01-preview' existing = {
  name: eventHubNamespaceName
}

resource eventHub 'Microsoft.EventHub/namespaces/eventhubs@2023-01-01-preview' existing = {
  parent: eventHubNamespace
  name: eventHubName
}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2023-11-15' existing = {
  name: cosmosAccountName
}

resource storageRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(photosContainer.id, principalId, storageBlobDataContributorRoleId)
  scope: photosContainer
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

resource eventHubRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(eventHub.id, principalId, eventHubDataSenderRoleId)
  scope: eventHub
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', eventHubDataSenderRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

resource facesReaderRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2023-11-15' = {
  name: guid(cosmosAccount.id, principalId, facesContainerName, cosmosDataReaderRoleId)
  parent: cosmosAccount
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/${cosmosDataReaderRoleId}'
    principalId: principalId
    scope: '${cosmosAccount.id}/dbs/${cosmosDatabaseName}/colls/${facesContainerName}'
  }
}

resource uploadsContributorRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2023-11-15' = {
  name: guid(cosmosAccount.id, principalId, uploadsContainerName, cosmosDataContributorRoleId)
  parent: cosmosAccount
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/${cosmosDataContributorRoleId}'
    principalId: principalId
    scope: '${cosmosAccount.id}/dbs/${cosmosDatabaseName}/colls/${uploadsContainerName}'
  }
}

resource reviewsContributorRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2023-11-15' = {
  name: guid(cosmosAccount.id, principalId, reviewsContainerName, cosmosDataContributorRoleId)
  parent: cosmosAccount
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/${cosmosDataContributorRoleId}'
    principalId: principalId
    scope: '${cosmosAccount.id}/dbs/${cosmosDatabaseName}/colls/${reviewsContainerName}'
  }
}
