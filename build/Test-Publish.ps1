[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDir,
    [switch]$SkipApp,
    [int]$PipeTimeoutSeconds = 60,
    [int]$AppAliveSeconds = 8
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $PublishDir -PathType Container)) { throw "$PublishDir not found" }
$publish = (Resolve-Path -LiteralPath $PublishDir).Path

function Stop-Started {
    param($Process)
    if ($Process) {
        try {
            if (-not $Process.HasExited) {
                Stop-Process -Id $Process.Id -Force -ErrorAction SilentlyContinue
                $Process.WaitForExit(10000) | Out-Null
            }
        }
        catch { }
    }
}

function Read-TextOrEmpty {
    param([string]$Path)
    if (Test-Path -LiteralPath $Path) { return [string](Get-Content -LiteralPath $Path -Raw) }
    return ''
}

function Limit-Text {
    param([string]$Text, [int]$Max = 1500)
    if ($Text.Length -le $Max) { return $Text }
    return $Text.Substring(0, $Max) + '... (truncated)'
}

function Test-ServiceSmoke {
    $serviceExe = Join-Path $publish 'Service\GapActivator.Service.exe'
    if (-not (Test-Path -LiteralPath $serviceExe)) { throw "Service smoke: $serviceExe not found" }

    $id = [Guid]::NewGuid().ToString('N')
    $pipeName = "GapActivator.Smoke.$id"
    $pipePath = "\\.\pipe\$pipeName"
    $tempRoot = [IO.Path]::GetTempPath()
    $temp = Join-Path $tempRoot "gapactivator-smoke-$id"
    $stdoutFile = Join-Path $tempRoot "gapactivator-smoke-$id.out.txt"
    $stderrFile = Join-Path $tempRoot "gapactivator-smoke-$id.err.txt"
    $process = $null
    try {
        New-Item -ItemType Directory -Path $temp | Out-Null
        $arguments = "--GapActivator:DataRoot=`"$temp`" --GapActivator:Ipc:PipeName=$pipeName"
        $process = Start-Process -FilePath $serviceExe -ArgumentList $arguments -WorkingDirectory (Split-Path -Parent $serviceExe) `
            -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdoutFile -RedirectStandardError $stderrFile
        # Reading the handle keeps the exit code available after the process exits.
        $null = $process.Handle

        $deadline = [DateTime]::UtcNow.AddSeconds($PipeTimeoutSeconds)
        $found = $false
        while (-not $found -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 250
            if ($process.HasExited) {
                $err = Read-TextOrEmpty $stderrFile
                $out = Read-TextOrEmpty $stdoutFile
                throw "Service smoke: GapActivator.Service.exe exited early with code $($process.ExitCode). stderr: $(Limit-Text $err) stdout: $(Limit-Text $out)"
            }
            $found = [IO.Directory]::GetFiles('\\.\pipe\') -contains $pipePath
        }
        if (-not $found) { throw "Service smoke: pipe '$pipeName' did not appear within $PipeTimeoutSeconds s" }
        Write-Host "Service smoke OK: pipe $pipeName appeared"
    }
    finally {
        Stop-Started $process
        foreach ($path in @($temp, $stdoutFile, $stderrFile)) {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction SilentlyContinue }
        }
    }
}

function Test-AppSmoke {
    $appExe = Join-Path $publish 'GapActivator.exe'
    if (-not (Test-Path -LiteralPath $appExe)) { throw "App smoke: $appExe not found" }

    $process = $null
    try {
        # The app ignores a LOCALAPPDATA override, so it may write app-settings.json into the real
        # %LOCALAPPDATA%\GapActivator. This script never deletes that folder or anything in it.
        $process = Start-Process -FilePath $appExe -ArgumentList '--minimized' -WorkingDirectory $publish -PassThru
        Start-Sleep -Seconds $AppAliveSeconds
        $process.Refresh()
        if ($process.HasExited) { throw "App smoke: GapActivator.exe exited early with code $($process.ExitCode)" }
        Write-Host "App smoke OK: still running after $AppAliveSeconds s"
    }
    finally {
        Stop-Started $process
    }
}

Test-ServiceSmoke
if (-not $SkipApp) { Test-AppSmoke }
Write-Host "Publish OK: $publish"
