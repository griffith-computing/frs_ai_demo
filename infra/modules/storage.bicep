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

@description('Name of the storage account. Must be globally unique, lowercase, 3-24 chars.')
param storageAccountName string

@description('Azure region for the storage account.')
param location string

@description('Set to true to reuse an existing storage account in this resource group.')
param useExistingStorageAccount bool = false

@description('Name of the existing storage account to reuse. Required when useExistingStorageAccount is true.')
param existingStorageAccountName string = ''

@description('Name of the blob container used to store uploaded photos.')
param photosContainerName string = 'photos'

var effectiveStorageAccountName = useExistingStorageAccount ? existingStorageAccountName : storageAccountName

resource existingStorageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = if (useExistingStorageAccount) {
  name: existingStorageAccountName
}

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = if (!useExistingStorageAccount) {
  name: storageAccountName
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    allowSharedKeyAccess: false
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Disabled'
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  name: '${effectiveStorageAccountName}/default'
  dependsOn: [
    existingStorageAccount
    storageAccount
  ]
}

resource photosContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: photosContainerName
  properties: {
    publicAccess: 'None'
  }
}

output storageAccountId string = useExistingStorageAccount ? existingStorageAccount.id : storageAccount.id
output storageAccountName string = effectiveStorageAccountName
output blobEndpoint string = useExistingStorageAccount ? existingStorageAccount!.properties.primaryEndpoints.blob : storageAccount!.properties.primaryEndpoints.blob
