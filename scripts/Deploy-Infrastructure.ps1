[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ResourceGroupName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$Location,

    [ValidatePattern('^[a-z0-9]{3,12}$')]
    [string]$NamePrefix = 'frsaidemo',

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$EntraClientId,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$UploadApiClientId,

    [securestring]$EntraClientSecret,

    [string]$EntraTenantId,

    [string]$DynamicPersonGroupId = 'frs-ai-demo-group',

    [switch]$UseExistingStorageAccount,
    [string]$ExistingStorageAccountName,
    [switch]$UseExistingEventHubNamespace,
    [string]$ExistingEventHubNamespaceName,
    [switch]$UseExistingCosmosAccount,
    [string]$ExistingCosmosAccountName,
    [switch]$UseExistingFaceAccount,
    [string]$ExistingFaceAccountName,
    [switch]$UseExistingFunctionApp,
    [string]$ExistingFunctionAppName,
    [string]$ExistingFunctionAppPlanName,
    [switch]$UseExistingWebApp,
    [string]$ExistingWebAppName,
    [string]$ExistingWebAppPlanName,

    [switch]$BulkUploadsEnabled,

    [ValidateRange(1, 100)]
    [int]$BulkUploadsMaxFiles = 100,

    [ValidateRange(1, 10)]
    [int]$BulkUploadsMaxConcurrency = 3,

    [ValidateNotNullOrEmpty()]
    [string]$BulkUploadsImportSchedule = '0 */1 * * * *',

    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$BulkUploadSourcesFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-AzCli {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed: az $($Arguments -join ' ')"
    }
}

function Assert-ReusedResourceName {
    param(
        [bool]$IsReused,
        [string[]]$Names,
        [string]$ResourceDescription
    )

    if ($IsReused -and ($Names | Where-Object { [string]::IsNullOrWhiteSpace($_) })) {
        throw "$ResourceDescription is configured for reuse, so all corresponding existing resource names are required."
    }
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI is required. Install it from https://learn.microsoft.com/cli/azure/install-azure-cli-windows.'
}

Assert-ReusedResourceName $UseExistingStorageAccount @($ExistingStorageAccountName) 'Storage account'
Assert-ReusedResourceName $UseExistingEventHubNamespace @($ExistingEventHubNamespaceName) 'Event Hubs namespace'
Assert-ReusedResourceName $UseExistingCosmosAccount @($ExistingCosmosAccountName) 'Cosmos DB account'
Assert-ReusedResourceName $UseExistingFaceAccount @($ExistingFaceAccountName) 'Azure AI Face account'
Assert-ReusedResourceName $UseExistingFunctionApp @($ExistingFunctionAppName, $ExistingFunctionAppPlanName) 'Function App'
Assert-ReusedResourceName $UseExistingWebApp @($ExistingWebAppName, $ExistingWebAppPlanName) 'Web App'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$templateFile = Join-Path $repositoryRoot 'infra\main.bicep'
if (-not (Test-Path $templateFile -PathType Leaf)) {
    throw "Bicep template was not found at '$templateFile'."
}

$bulkUploadSources = '[]'
if ($BulkUploadSourcesFile) {
    $bulkUploadSources = Get-Content -Path $BulkUploadSourcesFile -Raw
    try {
        $bulkUploadSources | ConvertFrom-Json -ErrorAction Stop | Out-Null
    }
    catch {
        throw "Bulk upload sources file '$BulkUploadSourcesFile' does not contain valid JSON."
    }
}

if (-not $EntraClientSecret) {
    $EntraClientSecret = Read-Host 'Entra web app client secret' -AsSecureString
}

$plainSecret = $null
try {
    $plainSecret = [System.Net.NetworkCredential]::new('', $EntraClientSecret).Password

    Invoke-AzCli @('bicep', 'build', '--file', $templateFile, '--stdout')

    $parameters = @(
        "namePrefix=$NamePrefix",
        "location=$Location",
        "dynamicPersonGroupId=$DynamicPersonGroupId",
        "entraClientId=$EntraClientId",
        "entraClientSecret=$plainSecret",
        "uploadApiClientId=$UploadApiClientId",
        "useExistingStorageAccount=$($UseExistingStorageAccount.IsPresent.ToString().ToLowerInvariant())",
        "existingStorageAccountName=$ExistingStorageAccountName",
        "useExistingEventHubNamespace=$($UseExistingEventHubNamespace.IsPresent.ToString().ToLowerInvariant())",
        "existingEventHubNamespaceName=$ExistingEventHubNamespaceName",
        "useExistingCosmosAccount=$($UseExistingCosmosAccount.IsPresent.ToString().ToLowerInvariant())",
        "existingCosmosAccountName=$ExistingCosmosAccountName",
        "useExistingFaceAccount=$($UseExistingFaceAccount.IsPresent.ToString().ToLowerInvariant())",
        "existingFaceAccountName=$ExistingFaceAccountName",
        "useExistingFunctionApp=$($UseExistingFunctionApp.IsPresent.ToString().ToLowerInvariant())",
        "existingFunctionAppName=$ExistingFunctionAppName",
        "existingFunctionAppPlanName=$ExistingFunctionAppPlanName",
        "useExistingWebApp=$($UseExistingWebApp.IsPresent.ToString().ToLowerInvariant())",
        "existingWebAppName=$ExistingWebAppName",
        "existingWebAppPlanName=$ExistingWebAppPlanName",
        "bulkUploadsEnabled=$($BulkUploadsEnabled.IsPresent.ToString().ToLowerInvariant())",
        "bulkUploadsMaxFiles=$BulkUploadsMaxFiles",
        "bulkUploadsMaxConcurrency=$BulkUploadsMaxConcurrency",
        "bulkUploadSources=$bulkUploadSources",
        "bulkUploadsImportSchedule=$BulkUploadsImportSchedule"
    )

    if ($EntraTenantId) {
        $parameters += "entraTenantId=$EntraTenantId"
    }

    $deploymentArguments = @(
        'deployment', 'group', 'what-if',
        '--resource-group', $ResourceGroupName,
        '--template-file', $templateFile,
        '--parameters'
    ) + $parameters

    if ($WhatIfPreference) {
        Invoke-AzCli $deploymentArguments
        return
    }

    Invoke-AzCli @('group', 'create', '--name', $ResourceGroupName, '--location', $Location)
    $deploymentArguments[2] = 'create'
    Invoke-AzCli $deploymentArguments
}
finally {
    $plainSecret = $null
    Remove-Variable plainSecret -ErrorAction SilentlyContinue
}
