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

@description('Name of the Event Hub Namespace. Must be globally unique.')
param namespaceName string

@description('Azure region for the Event Hub namespace.')
param location string

@description('Name of the Event Hub used to carry photo-upload events.')
param eventHubName string = 'photo-events'

@description('Name of the consumer group used by the processing function.')
param consumerGroupName string = 'process-photo-function'

@description('SKU for the Event Hub namespace.')
@allowed([
  'Basic'
  'Standard'
])
param skuName string = 'Standard'

resource namespace 'Microsoft.EventHub/namespaces@2023-01-01-preview' = {
  name: namespaceName
  location: location
  sku: {
    name: skuName
    tier: skuName
    capacity: 1
  }
  properties: {
    minimumTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
    disableLocalAuth: false
  }
}

resource eventHub 'Microsoft.EventHub/namespaces/eventhubs@2023-01-01-preview' = {
  parent: namespace
  name: eventHubName
  properties: {
    messageRetentionInDays: 1
    partitionCount: 2
  }
}

resource consumerGroup 'Microsoft.EventHub/namespaces/eventhubs/consumergroups@2023-01-01-preview' = {
  parent: eventHub
  name: consumerGroupName
}

output namespaceId string = namespace.id
output namespaceName string = namespace.name
output eventHubName string = eventHub.name
output consumerGroupName string = consumerGroup.name
output fullyQualifiedNamespace string = '${namespace.name}.servicebus.windows.net'
