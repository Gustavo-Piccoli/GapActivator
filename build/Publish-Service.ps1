[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'win-arm64')][string]$Runtime,
    [Parameter(Mandatory)][string]$Version,
    [string]$OutputDir
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw "Invalid version '$Version'" }

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$arch = $Runtime -replace '^win-', ''
if (-not $OutputDir) { $OutputDir = Join-Path $repo "artifacts\publish\$arch\Service" }

# Clean only this folder, so that Publish-App.ps1 and this script can run in either order.
if (Test-Path -LiteralPath $OutputDir) { Remove-Item -LiteralPath $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

& dotnet publish (Join-Path $repo 'src\GapActivator.Service') -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false "-p:Version=$Version" -o $OutputDir | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Get-ChildItem -LiteralPath $OutputDir -Filter '*.pdb' -File -Recurse | Remove-Item -Force

$exe = Join-Path $OutputDir 'GapActivator.Service.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw "GapActivator.Service.exe missing from publish output $OutputDir" }
if (-not (Test-Path -LiteralPath (Join-Path $OutputDir 'e_sqlite3.dll'))) { throw "e_sqlite3.dll missing from publish output $OutputDir" }

$productVersion = (Get-Item -LiteralPath $exe).VersionInfo.ProductVersion
if (-not $productVersion.StartsWith($Version)) { throw "GapActivator.Service.exe ProductVersion '$productVersion' does not start with requested version '$Version'" }

$OutputDir
