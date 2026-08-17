@description('Name of the user-assigned managed identity.')
param identityName string

@description('Azure region for the identity.')
param location string

resource userAssignedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
}

output identityId string = userAssignedIdentity.id
output identityPrincipalId string = userAssignedIdentity.properties.principalId
output identityClientId string = userAssignedIdentity.properties.clientId
