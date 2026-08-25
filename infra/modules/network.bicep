@description('Name of the virtual network.')
param vnetName string

@description('Azure region for the virtual network.')
param location string

@description('Address space for the virtual network.')
param addressPrefix string = '10.20.0.0/16'

@description('Address prefix for the subnet delegated to the Function App for outbound VNet integration.')
param integrationSubnetPrefix string = '10.20.0.0/24'

@description('Address prefix for the subnet used by private endpoints.')
param privateEndpointSubnetPrefix string = '10.20.1.0/24'

@description('Address prefix for the subnet delegated to the Linux reviewer web app.')
param webIntegrationSubnetPrefix string = '10.20.2.0/24'

resource vnet 'Microsoft.Network/virtualNetworks@2023-09-01' = {
  name: vnetName
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        addressPrefix
      ]
    }
    subnets: [
      {
        name: 'snet-integration'
        properties: {
          addressPrefix: integrationSubnetPrefix
          delegations: [
            {
              name: 'delegation-serverfarms'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
      {
        name: 'snet-privateendpoints'
        properties: {
          addressPrefix: privateEndpointSubnetPrefix
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
      {
        name: 'snet-web-integration'
        properties: {
          addressPrefix: webIntegrationSubnetPrefix
          delegations: [
            {
              name: 'delegation-serverfarms-web'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
    ]
  }
}

output vnetId string = vnet.id
output vnetName string = vnet.name
output integrationSubnetId string = vnet.properties.subnets[0].id
output privateEndpointSubnetId string = vnet.properties.subnets[1].id
output webIntegrationSubnetId string = vnet.properties.subnets[2].id
