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

@description('Name of the Cosmos DB account. Must be globally unique, lowercase.')
param accountName string

@description('Azure region for the Cosmos DB account.')
param location string

@description('Set to true to reuse an existing Cosmos DB account in this resource group.')
param useExistingCosmosAccount bool = false

@description('Name of the existing Cosmos DB account to reuse. Required when useExistingCosmosAccount is true.')
param existingCosmosAccountName string = ''

@description('Name of the SQL (NoSQL) database.')
param databaseName string = 'FacialRecognitionDb'

@description('Name of the container that stores per-person face recognition records.')
param facesContainerName string = 'Faces'

@description('Name of the container that stores durable photo-processing status.')
param uploadsContainerName string = 'Uploads'

@description('Name of the container that stores reviewer decisions.')
param reviewsContainerName string = 'Reviews'

var effectiveCosmosAccountName = useExistingCosmosAccount ? existingCosmosAccountName : accountName

resource existingCosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2023-11-15' existing = if (useExistingCosmosAccount) {
  name: existingCosmosAccountName
}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2023-11-15' = if (!useExistingCosmosAccount) {
  name: accountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    locations: [
      {
        locationName: location
        failoverPriority: 0
        isZoneRedundant: false
      }
    ]
    disableLocalAuth: true
    publicNetworkAccess: 'Disabled'
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-11-15' = {
  name: '${effectiveCosmosAccountName}/${databaseName}'
  properties: {
    resource: {
      id: databaseName
    }
  }
  dependsOn: [
    existingCosmosAccount
    cosmosAccount
  ]
}

resource facesContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-11-15' = {
  parent: database
  name: facesContainerName
  properties: {
    resource: {
      id: facesContainerName
      partitionKey: {
        paths: [
          '/personId'
        ]
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
      }
    }
  }
}

resource uploadsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-11-15' = {
  parent: database
  name: uploadsContainerName
  properties: {
    resource: {
      id: uploadsContainerName
      partitionKey: {
        paths: [
          '/id'
        ]
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
      }
    }
  }
}

resource reviewsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-11-15' = {
  parent: database
  name: reviewsContainerName
  properties: {
    resource: {
      id: reviewsContainerName
      partitionKey: {
        paths: [
          '/personId'
        ]
        kind: 'Hash'
      }
      indexingPolicy: {
        indexingMode: 'consistent'
        automatic: true
      }
    }
  }
}

output cosmosAccountId string = useExistingCosmosAccount ? existingCosmosAccount.id : cosmosAccount.id
output cosmosAccountName string = effectiveCosmosAccountName
output cosmosEndpoint string = useExistingCosmosAccount ? existingCosmosAccount!.properties.documentEndpoint : cosmosAccount!.properties.documentEndpoint
output databaseName string = databaseName
output facesContainerName string = facesContainerName
output uploadsContainerName string = uploadsContainerName
output reviewsContainerName string = reviewsContainerName
