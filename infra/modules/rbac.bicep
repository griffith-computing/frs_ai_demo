@description('Principal ID of the user-assigned managed identity to grant access to.')
param principalId string

@description('Name of the storage account.')
param storageAccountName string

@description('Name of the Event Hub namespace.')
param eventHubNamespaceName string

@description('Name of the Cognitive Services (Face API) account.')
param faceAccountName string

@description('Name of the Cosmos DB account.')
param cosmosAccountName string

// Built-in role definition IDs
// Storage Blob Data Owner (not Contributor): the app creates containers on demand
// (poison-messages) which requires the containers/write data action that Contributor lacks.
var storageBlobDataOwnerRoleId = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
var eventHubDataOwnerRoleId = 'f526a384-b230-433a-b45c-95f59c4a2dec'
var cognitiveServicesUserRoleId = 'a97b65f3-24c7-4388-baec-2e87135dc908'
// Cosmos DB's built-in "Cosmos DB Built-in Data Contributor" SQL role definition ID (fixed GUID suffix on every account)
var cosmosDataContributorRoleId = '00000000-0000-0000-0000-000000000002'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

resource eventHubNamespace 'Microsoft.EventHub/namespaces@2023-01-01-preview' existing = {
  name: eventHubNamespaceName
}

resource faceAccount 'Microsoft.CognitiveServices/accounts@2023-05-01' existing = {
  name: faceAccountName
}

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2023-11-15' existing = {
  name: cosmosAccountName
}

resource storageRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, principalId, storageBlobDataOwnerRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataOwnerRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

resource eventHubRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(eventHubNamespace.id, principalId, eventHubDataOwnerRoleId)
  scope: eventHubNamespace
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', eventHubDataOwnerRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

resource faceRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(faceAccount.id, principalId, cognitiveServicesUserRoleId)
  scope: faceAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

// Cosmos DB uses its own data-plane RBAC system (sqlRoleAssignments), separate from
// standard Azure RBAC role assignments used for Storage/EventHub/Cognitive Services above.
resource cosmosRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2023-11-15' = {
  name: guid(cosmosAccount.id, principalId, cosmosDataContributorRoleId)
  parent: cosmosAccount
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/${cosmosDataContributorRoleId}'
    principalId: principalId
    scope: cosmosAccount.id
  }
}
