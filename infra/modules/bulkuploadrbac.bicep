//----------------------------------------------------------------------------------
// THIS CODE AND INFORMATION ARE PROVIDED "AS IS" WITHOUT WARRANTY OF ANY KIND,
// EITHER EXPRESSED OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE IMPLIED WARRANTIES
// OF MERCHANTABILITY AND/OR FITNESS FOR A PARTICULAR PURPOSE.
//
// Copyright (c) Microsoft Corporation. All rights reserved.
//----------------------------------------------------------------------------------

@description('Name of an allowlisted source storage account in this resource group.')
param storageAccountName string

@description('Name of the allowlisted source blob container.')
param containerName string

@description('Principal ID of the reviewer web app managed identity.')
param webPrincipalId string

@description('Whether reviewers may load source blobs directly from this container.')
param allowInPlace bool

@description('Principal ID of the ingestion Function App managed identity.')
param functionPrincipalId string

var storageBlobDataReaderRoleId = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' existing = {
  parent: storageAccount
  name: 'default'
}

resource sourceContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' existing = {
  parent: blobService
  name: containerName
}

resource webReaderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (allowInPlace) {
  name: guid(sourceContainer.id, webPrincipalId, storageBlobDataReaderRoleId)
  scope: sourceContainer
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataReaderRoleId)
    principalId: webPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource functionReaderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(sourceContainer.id, functionPrincipalId, storageBlobDataReaderRoleId)
  scope: sourceContainer
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataReaderRoleId)
    principalId: functionPrincipalId
    principalType: 'ServicePrincipal'
  }
}
