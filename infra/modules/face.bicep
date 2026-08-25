@description('Name of the Azure AI Face resource.')
param faceAccountName string

@description('Azure region for the Face API resource. Face API is only available in a subset of regions.')
param location string

@description('SKU for the Face API resource.')
param skuName string = 'S0'

resource faceAccount 'Microsoft.CognitiveServices/accounts@2023-05-01' = {
  name: faceAccountName
  location: location
  kind: 'Face'
  sku: {
    name: skuName
  }
  properties: {
    customSubDomainName: faceAccountName
    publicNetworkAccess: 'Disabled'
    disableLocalAuth: false
    networkAcls: {
      defaultAction: 'Deny'
    }
  }
}

output faceAccountId string = faceAccount.id
output faceAccountName string = faceAccount.name
output faceEndpoint string = faceAccount.properties.endpoint
