[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Directory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { throw "$Directory not found" }
$dir = (Resolve-Path -LiteralPath $Directory).Path

$files = @(Get-ChildItem -LiteralPath $dir -File | Where-Object { $_.Name -like 'GapActivator-Setup-*.exe' })
if ($files.Count -eq 0) { throw "No GapActivator-Setup-*.exe files in $dir" }

$names = [string[]]@($files | ForEach-Object { $_.Name })
[Array]::Sort($names, [StringComparer]::Ordinal)

$sb = New-Object Text.StringBuilder
foreach ($name in $names) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $dir $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    [void]$sb.Append($hash).Append('  ').Append($name).Append("`n")
}

$out = Join-Path $dir 'SHA256SUMS.txt'
[IO.File]::WriteAllText($out, $sb.ToString(), (New-Object Text.UTF8Encoding($false)))
$out
