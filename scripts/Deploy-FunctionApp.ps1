[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ResourceGroupName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$FunctionAppName
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

function New-DeploymentZip {
    param(
        [Parameter(Mandatory)]
        [string]$SourceDirectory,

        [Parameter(Mandatory)]
        [string]$ZipPath
    )

    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $fileStream = [System.IO.File]::Open($ZipPath, [System.IO.FileMode]::CreateNew)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $fileStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            foreach ($file in Get-ChildItem -Path $SourceDirectory -Recurse -File) {
                $entryName = $file.FullName.Substring($SourceDirectory.Length + 1).Replace('\', '/')
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $archive,
                    $file.FullName,
                    $entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $fileStream.Dispose()
    }
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI is required. Install it from https://learn.microsoft.com/cli/azure/install-azure-cli-windows.'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK is required. Install it from https://dotnet.microsoft.com/download.'
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $repositoryRoot 'src\FunctionApp\FrsAiDemo.FunctionApp.csproj'
if (-not (Test-Path $projectFile -PathType Leaf)) {
    throw "Function App project was not found at '$projectFile'."
}

$packageDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "frs-ai-demo-function-$([guid]::NewGuid())"
$zipPath = "$packageDirectory.zip"

try {
    & dotnet publish $projectFile '--configuration' 'Release' '--output' $packageDirectory
    if ($LASTEXITCODE -ne 0) {
        throw 'Function App publish failed.'
    }

    New-DeploymentZip -SourceDirectory $packageDirectory -ZipPath $zipPath
    Invoke-AzCli @(
        'functionapp', 'deployment', 'source', 'config-zip',
        '--resource-group', $ResourceGroupName,
        '--name', $FunctionAppName,
        '--src', $zipPath
    )
}
finally {
    if (Test-Path $packageDirectory) {
        Remove-Item -Path $packageDirectory -Recurse -Force
    }

    if (Test-Path $zipPath) {
        Remove-Item -Path $zipPath -Force
    }
}
