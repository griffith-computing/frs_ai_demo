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

@description('Azure region for the private endpoints (should match the target resources region).')
param location string

@description('Resource ID of the VNet subnet to deploy private endpoints into.')
param privateEndpointSubnetId string

@description('Resource ID of the VNet to link the private DNS zones to.')
param vnetId string

@description('Resource ID of the storage account.')
param storageAccountId string

@description('Name of the storage account (used to build unique private endpoint names).')
param storageAccountName string

@description('Resource ID of the Event Hub namespace.')
param eventHubNamespaceId string

@description('Name of the Event Hub namespace.')
param eventHubNamespaceName string

@description('Resource ID of the Cosmos DB account.')
param cosmosAccountId string

@description('Name of the Cosmos DB account.')
param cosmosAccountName string

@description('Resource ID of the Azure AI Face (Cognitive Services) account.')
param faceAccountId string

@description('Name of the Face API account.')
param faceAccountName string

// Private DNS zone names are fixed by Azure for each service's Private Link integration.
var blobDnsZoneName = 'privatelink.blob.core.windows.net'
var queueDnsZoneName = 'privatelink.queue.core.windows.net'
var tableDnsZoneName = 'privatelink.table.core.windows.net'
var cosmosDnsZoneName = 'privatelink.documents.azure.com'
var serviceBusDnsZoneName = 'privatelink.servicebus.windows.net'
var cognitiveServicesDnsZoneName = 'privatelink.cognitiveservices.azure.com'

resource blobDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: blobDnsZoneName
  location: 'global'
}

resource queueDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: queueDnsZoneName
  location: 'global'
}

resource tableDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: tableDnsZoneName
  location: 'global'
}

resource cosmosDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: cosmosDnsZoneName
  location: 'global'
}

resource serviceBusDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: serviceBusDnsZoneName
  location: 'global'
}

resource cognitiveServicesDnsZone 'Microsoft.Network/privateDnsZones@2020-06-01' = {
  name: cognitiveServicesDnsZoneName
  location: 'global'
}

resource blobDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: blobDnsZone
  name: 'link-${uniqueString(vnetId)}'
  location: 'global'
  properties: {
    virtualNetwork: { id: vnetId }
    registrationEnabled: false
  }
}

resource queueDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: queueDnsZone
  name: 'link-${uniqueString(vnetId)}'
  location: 'global'
  properties: {
    virtualNetwork: { id: vnetId }
    registrationEnabled: false
  }
}

resource tableDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: tableDnsZone
  name: 'link-${uniqueString(vnetId)}'
  location: 'global'
  properties: {
    virtualNetwork: { id: vnetId }
    registrationEnabled: false
  }
}

resource cosmosDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: cosmosDnsZone
  name: 'link-${uniqueString(vnetId)}'
  location: 'global'
  properties: {
    virtualNetwork: { id: vnetId }
    registrationEnabled: false
  }
}

resource serviceBusDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: serviceBusDnsZone
  name: 'link-${uniqueString(vnetId)}'
  location: 'global'
  properties: {
    virtualNetwork: { id: vnetId }
    registrationEnabled: false
  }
}

resource cognitiveServicesDnsZoneLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = {
  parent: cognitiveServicesDnsZone
  name: 'link-${uniqueString(vnetId)}'
  location: 'global'
  properties: {
    virtualNetwork: { id: vnetId }
    registrationEnabled: false
  }
}

resource blobPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-09-01' = {
  name: 'pe-${storageAccountName}-blob'
  location: location
  properties: {
    subnet: { id: privateEndpointSubnetId }
    privateLinkServiceConnections: [
      {
        name: 'plsc-blob'
        properties: {
          privateLinkServiceId: storageAccountId
          groupIds: [ 'blob' ]
        }
      }
    ]
  }
}

resource blobPrivateEndpointDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = {
  parent: blobPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      { name: 'blob', properties: { privateDnsZoneId: blobDnsZone.id } }
    ]
  }
}

resource queuePrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-09-01' = {
  name: 'pe-${storageAccountName}-queue'
  location: location
  properties: {
    subnet: { id: privateEndpointSubnetId }
    privateLinkServiceConnections: [
      {
        name: 'plsc-queue'
        properties: {
          privateLinkServiceId: storageAccountId
          groupIds: [ 'queue' ]
        }
      }
    ]
  }
}

resource queuePrivateEndpointDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = {
  parent: queuePrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      { name: 'queue', properties: { privateDnsZoneId: queueDnsZone.id } }
    ]
  }
}

resource tablePrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-09-01' = {
  name: 'pe-${storageAccountName}-table'
  location: location
  properties: {
    subnet: { id: privateEndpointSubnetId }
    privateLinkServiceConnections: [
      {
        name: 'plsc-table'
        properties: {
          privateLinkServiceId: storageAccountId
          groupIds: [ 'table' ]
        }
      }
    ]
  }
}

resource tablePrivateEndpointDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = {
  parent: tablePrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      { name: 'table', properties: { privateDnsZoneId: tableDnsZone.id } }
    ]
  }
}

resource cosmosPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-09-01' = {
  name: 'pe-${cosmosAccountName}-sql'
  location: location
  properties: {
    subnet: { id: privateEndpointSubnetId }
    privateLinkServiceConnections: [
      {
        name: 'plsc-cosmos'
        properties: {
          privateLinkServiceId: cosmosAccountId
          groupIds: [ 'Sql' ]
        }
      }
    ]
  }
}

resource cosmosPrivateEndpointDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = {
  parent: cosmosPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      { name: 'cosmos', properties: { privateDnsZoneId: cosmosDnsZone.id } }
    ]
  }
}

resource eventHubPrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-09-01' = {
  name: 'pe-${eventHubNamespaceName}-namespace'
  location: location
  properties: {
    subnet: { id: privateEndpointSubnetId }
    privateLinkServiceConnections: [
      {
        name: 'plsc-eventhub'
        properties: {
          privateLinkServiceId: eventHubNamespaceId
          groupIds: [ 'namespace' ]
        }
      }
    ]
  }
}

resource eventHubPrivateEndpointDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = {
  parent: eventHubPrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      { name: 'servicebus', properties: { privateDnsZoneId: serviceBusDnsZone.id } }
    ]
  }
}

resource facePrivateEndpoint 'Microsoft.Network/privateEndpoints@2023-09-01' = {
  name: 'pe-${faceAccountName}-account'
  location: location
  properties: {
    subnet: { id: privateEndpointSubnetId }
    privateLinkServiceConnections: [
      {
        name: 'plsc-face'
        properties: {
          privateLinkServiceId: faceAccountId
          groupIds: [ 'account' ]
        }
      }
    ]
  }
}

resource facePrivateEndpointDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = {
  parent: facePrivateEndpoint
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      { name: 'cognitiveservices', properties: { privateDnsZoneId: cognitiveServicesDnsZone.id } }
    ]
  }
}
