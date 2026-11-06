[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDir,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidateSet('x64', 'arm64')][string]$Arch,
    [string]$OutputDir
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^(\d+\.\d+\.\d+)(-[0-9A-Za-z.-]+)?$') { throw "Invalid version '$Version'" }
$numericVersion = $Matches[1]
if (-not (Test-Path -LiteralPath $PublishDir -PathType Container)) { throw "$PublishDir not found" }

# The layout the installer expects: the app at the root and the service in Service\.
foreach ($required in @('GapActivator.exe', 'LICENSE.txt', 'Service\GapActivator.Service.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PublishDir $required) -PathType Leaf)) {
        throw "$required is missing from $PublishDir. Run Publish-App.ps1 and Publish-Service.ps1 first."
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script = Join-Path $repoRoot 'installer\GapActivator.iss'
if (-not (Test-Path -LiteralPath $script -PathType Leaf)) { throw "$script not found" }

if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot 'artifacts\dist' }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
$outputAbs = (Resolve-Path -LiteralPath $OutputDir).Path
$sourceAbs = (Resolve-Path -LiteralPath $PublishDir).Path

function Find-Iscc {
    if ($env:ISCC -and (Test-Path -LiteralPath $env:ISCC -PathType Leaf)) { return $env:ISCC }
    $command = Get-Command iscc -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $candidates = @()
    if ($env:ProgramFiles) { $candidates += Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe' }
    if (${env:ProgramFiles(x86)}) { $candidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
    if ($env:LOCALAPPDATA) { $candidates += Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    $null
}

$iscc = Find-Iscc
if (-not $iscc) { throw 'ISCC.exe (Inno Setup compiler) not found. Install Inno Setup 6 or set the ISCC environment variable to the full path of ISCC.exe.' }

& $iscc /Q "/DAppVersion=$Version" "/DNumericVersion=$numericVersion" "/DArch=$Arch" "/DSourceDir=$sourceAbs" "/DOutputDir=$outputAbs" $script
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

$setup = Join-Path $outputAbs "GapActivator-Setup-$Version-$Arch.exe"
if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) { throw "$setup was not produced" }
$setup
