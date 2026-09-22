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

@description('Name of the FaceLab web app.')
param webAppName string

@description('Azure region for the web app.')
param location string

@description('Resource ID of the user-assigned identity attached to the web app.')
param userAssignedIdentityId string

@description('Client ID of the user-assigned identity.')
param userAssignedIdentityClientId string

@description('Resource ID of the dedicated App Service VNet integration subnet.')
param integrationSubnetId string

@description('Application Insights connection string.')
param appInsightsConnectionString string

@description('Log Analytics workspace resource ID for App Service diagnostics.')
param logAnalyticsWorkspaceId string

@description('Storage account containing FaceLab staged images.')
param storageAccountName string

@description('Container containing FaceLab staged images.')
param faceLabImagesContainerName string = 'facelab-images'

@description('Azure AI Face API endpoint.')
param faceApiEndpoint string

@description('Face API Dynamic Person Group id used by default.')
param dynamicPersonGroupId string = 'frs-ai-demo-group'

var planName = '${webAppName}-plan'

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  kind: 'linux'
  sku: {
    name: 'B1'
    tier: 'Basic'
  }
  properties: {
    reserved: true
  }
}

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentityId}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    virtualNetworkSubnetId: integrationSubnetId
    vnetRouteAllEnabled: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health'
      appSettings: [
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        { name: 'AZURE_CLIENT_ID', value: userAssignedIdentityClientId }
        { name: 'WEBSITE_VNET_ROUTE_ALL', value: '1' }
        { name: 'WEBSITE_DNS_SERVER', value: '168.63.129.16' }
        { name: 'FaceLab__Endpoint', value: faceApiEndpoint }
        { name: 'FaceLab__AuthMode', value: 'DefaultAzureCredential' }
        { name: 'FaceLab__DynamicPersonGroupId', value: dynamicPersonGroupId }
        { name: 'FaceLabStorage__AccountName', value: storageAccountName }
        { name: 'FaceLabStorage__ContainerName', value: faceLabImagesContainerName }
      ]
    }
  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: '${webAppName}-diagnostics'
  scope: webApp
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        categoryGroup: 'allLogs'
        enabled: true
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
      }
    ]
  }
}

output webAppId string = webApp.id
output webAppName string = webApp.name
output webAppHostName string = webApp.properties.defaultHostName
