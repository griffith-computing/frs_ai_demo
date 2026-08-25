@description('Name of the Cosmos DB account. Must be globally unique, lowercase.')
param accountName string

@description('Azure region for the Cosmos DB account.')
param location string

@description('Name of the SQL (NoSQL) database.')
param databaseName string = 'FacialRecognitionDb'

@description('Name of the container that stores per-person face recognition records.')
param facesContainerName string = 'Faces'

@description('Name of the container that stores durable photo-processing status.')
param uploadsContainerName string = 'Uploads'

@description('Name of the container that stores reviewer decisions.')
param reviewsContainerName string = 'Reviews'

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2023-11-15' = {
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
  parent: cosmosAccount
  name: databaseName
  properties: {
    resource: {
      id: databaseName
    }
  }
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

output cosmosAccountId string = cosmosAccount.id
output cosmosAccountName string = cosmosAccount.name
output cosmosEndpoint string = cosmosAccount.properties.documentEndpoint
output databaseName string = database.name
output facesContainerName string = facesContainer.name
output uploadsContainerName string = uploadsContainer.name
output reviewsContainerName string = reviewsContainer.name
