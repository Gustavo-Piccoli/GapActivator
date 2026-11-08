<#
.SYNOPSIS
  Generates the GapActivator brand icon (gapactivator.ico).
.DESCRIPTION
  Works in Windows PowerShell 5.1 and pwsh on Windows (System.Drawing only).
  The motif is a scheduled run: a solid white play triangle on a rounded square in the Busy blue (#2F6FED) of the
  app tokens, with four small clock-tick dots (12, 3, 6 and 9 o'clock) around it from 40 px up. At 16, 20, 24 and
  32 px the glyph is the triangle alone, snapped to whole pixels so it stays crisp. The tray icons are plain
  circles, so the square tile keeps the brand icon distinct from them.
  Stored as PNG entries at 16, 20, 24, 32, 40, 48, 64 and 256 px.
.PARAMETER OutputDirectory
  Folder that receives gapactivator.ico. Defaults to src/GapActivator.App/Assets/Icons.
.PARAMETER PreviewDirectory
  Optional folder that also receives one PNG per size, for looking at the frames. Not meant to be committed.
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$PreviewDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $PSScriptRoot '..\..\src\GapActivator.App\Assets\Icons'
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$brand = [System.Drawing.ColorTranslator]::FromHtml('#2F6FED')

function New-RoundedRectPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    # Tile: rounded square filling the canvas.
    $fill = New-Object System.Drawing.SolidBrush $brand
    $tile = New-RoundedRectPath 0 0 $size $size ($size * 0.22)
    $g.FillPath($fill, $tile)

    $white = [System.Drawing.Color]::White
    $c = $size / 2.0

    $whiteBrush = New-Object System.Drawing.SolidBrush $white
    if ($size -le 32) {
        # Simplified glyph: a larger triangle with every vertex on a whole pixel, so the left edge is crisp.
        $h = [Math]::Round($size * 0.62 / 2) * 2
        $w = [Math]::Round($h * 0.86)
        $left = [Math]::Round($c - ($w / 3.0))
        $top = $c - ($h / 2)
    }
    else {
        # Full glyph: the triangle, nudged right so its centroid (not its bounding box) sits at the center,
        # and four clock-tick dots around it.
        $h = $size * 0.40
        $w = $size * 0.35
        $left = $c - ($w / 3.0)
        $top = $c - ($h / 2)
        $dot = $size * 0.032
        $ring = $size * 0.36
        foreach ($angle in 0, 90, 180, 270) {
            $rad = $angle * [Math]::PI / 180.0
            $dx = $c + $ring * [Math]::Cos($rad)
            $dy = $c + $ring * [Math]::Sin($rad)
            $g.FillEllipse($whiteBrush, [single]($dx - $dot), [single]($dy - $dot), [single](2 * $dot), [single](2 * $dot))
        }
    }
    $points = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF([single]$left, [single]$top)),
        (New-Object System.Drawing.PointF([single]($left + $w), [single]$c)),
        (New-Object System.Drawing.PointF([single]$left, [single]($top + $h))))
    $g.FillPolygon($whiteBrush, $points)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    if ($PreviewDirectory) {
        $null = New-Item -ItemType Directory -Force -Path $PreviewDirectory
        [System.IO.File]::WriteAllBytes((Join-Path (Resolve-Path $PreviewDirectory) "frame-$size.png"), $bytes)
    }

    $whiteBrush.Dispose(); $tile.Dispose(); $fill.Dispose(); $ms.Dispose(); $g.Dispose(); $bmp.Dispose()
    # Unary comma keeps the byte[] from being unrolled by the pipeline.
    return , $bytes
}

$images = foreach ($s in $sizes) { , (New-IconPng $s) }

$null = New-Item -ItemType Directory -Force -Path $OutputDirectory
$target = Join-Path (Resolve-Path $OutputDirectory) 'gapactivator.ico'
$stream = [System.IO.File]::Create($target)
$w = New-Object System.IO.BinaryWriter $stream
$w.Write([uint16]0)               # reserved
$w.Write([uint16]1)               # type: icon
$w.Write([uint16]$sizes.Count)    # image count

$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }   # 0 means 256 in the ICO directory
    $w.Write([byte]$dim)
    $w.Write([byte]$dim)
    $w.Write([byte]0)             # palette
    $w.Write([byte]0)             # reserved
    $w.Write([uint16]1)           # planes
    $w.Write([uint16]32)          # bits per pixel
    $w.Write([uint32]$images[$i].Length)
    $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write([byte[]]$img) }
$w.Dispose()
$stream.Dispose()
Write-Host "Wrote $target"
