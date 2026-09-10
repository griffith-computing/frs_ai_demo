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

@description('Set to true to reuse an existing Event Hubs namespace in this resource group.')
param useExistingEventHubNamespace bool = false

@description('Name of the existing Event Hubs namespace to reuse. Required when useExistingEventHubNamespace is true.')
param existingEventHubNamespaceName string = ''

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

var effectiveNamespaceName = useExistingEventHubNamespace ? existingEventHubNamespaceName : namespaceName

resource existingNamespace 'Microsoft.EventHub/namespaces@2023-01-01-preview' existing = if (useExistingEventHubNamespace) {
  name: existingEventHubNamespaceName
}

resource namespace 'Microsoft.EventHub/namespaces@2023-01-01-preview' = if (!useExistingEventHubNamespace) {
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
  name: '${effectiveNamespaceName}/${eventHubName}'
  properties: {
    messageRetentionInDays: 1
    partitionCount: 2
  }
  dependsOn: [
    existingNamespace
    namespace
  ]
}

resource consumerGroup 'Microsoft.EventHub/namespaces/eventhubs/consumergroups@2023-01-01-preview' = {
  parent: eventHub
  name: consumerGroupName
}

output namespaceId string = useExistingEventHubNamespace ? existingNamespace.id : namespace.id
output namespaceName string = effectiveNamespaceName
output eventHubName string = eventHubName
output consumerGroupName string = consumerGroupName
output fullyQualifiedNamespace string = '${effectiveNamespaceName}.servicebus.windows.net'
