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

@description('Name of the Azure AI Face resource. Used to create a new account when useExistingFaceAccount is false.')
param faceAccountName string

@description('Azure region for the Face API resource. Face API is only available in a subset of regions.')
param location string

@description('SKU for the Face API resource.')
param skuName string = 'S0'

@description('Set to true to reuse an existing Azure AI Face account instead of creating a new one. The existing account must already exist in this deployment\'s resource group.')
param useExistingFaceAccount bool = false

@description('Name of the existing Azure AI Face (Cognitive Services, kind=Face) account to reuse. Required when useExistingFaceAccount is true; must be in this deployment\'s resource group.')
param existingFaceAccountName string = ''

resource existingFaceAccount 'Microsoft.CognitiveServices/accounts@2023-05-01' existing = if (useExistingFaceAccount) {
  name: existingFaceAccountName
}

resource faceAccount 'Microsoft.CognitiveServices/accounts@2023-05-01' = if (!useExistingFaceAccount) {
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

output faceAccountId string = useExistingFaceAccount ? existingFaceAccount.id : faceAccount.id
output faceAccountName string = useExistingFaceAccount ? existingFaceAccount.name : faceAccount.name
output faceEndpoint string = useExistingFaceAccount ? existingFaceAccount.properties.endpoint : faceAccount.properties.endpoint
