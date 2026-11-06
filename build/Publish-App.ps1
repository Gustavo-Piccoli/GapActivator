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
if (-not $OutputDir) { $OutputDir = Join-Path $repo "artifacts\publish\$arch" }
$obj = Join-Path $repo "artifacts\obj-publish\$arch"

# The AOT linker step needs vswhere; local machines often lack it on PATH.
if (-not (Get-Command vswhere -ErrorAction SilentlyContinue)) {
    $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
    if (Test-Path -LiteralPath (Join-Path $installer 'vswhere.exe')) { $env:PATH = "$installer;$env:PATH" }
}

if (Test-Path -LiteralPath $obj) { Remove-Item -LiteralPath $obj -Recurse -Force }
New-Item -ItemType Directory -Path $obj -Force | Out-Null

& dotnet publish (Join-Path $repo 'src\GapActivator.App') -c Release -r $Runtime "-p:Version=$Version" -o $obj | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

if (-not (Test-Path -LiteralPath (Join-Path $obj 'GapActivator.exe'))) { throw "GapActivator.exe missing from publish output $obj" }

# Clean the output folder but keep a Service subfolder: Publish-Service.ps1 fills it,
# and the order of the two scripts must not matter.
if (Test-Path -LiteralPath $OutputDir) {
    Get-ChildItem -LiteralPath $OutputDir -Force | Where-Object { $_.Name -ne 'Service' } | Remove-Item -Recurse -Force
}
else {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $obj 'GapActivator.exe') -Destination $OutputDir
Get-ChildItem -LiteralPath $obj -Filter '*.dll' -File | Copy-Item -Destination $OutputDir
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination (Join-Path $OutputDir 'LICENSE.txt')

$productVersion = (Get-Item -LiteralPath (Join-Path $OutputDir 'GapActivator.exe')).VersionInfo.ProductVersion
if (-not $productVersion.StartsWith($Version)) { throw "GapActivator.exe ProductVersion '$productVersion' does not start with requested version '$Version'" }

$OutputDir
