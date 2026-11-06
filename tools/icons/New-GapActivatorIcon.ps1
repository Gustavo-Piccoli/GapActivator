<#
.SYNOPSIS
  Generates the GapActivator brand icon (gapactivator.ico).
.DESCRIPTION
  Works in Windows PowerShell 5.1 and pwsh on Windows (System.Drawing only).
  The motif is a scheduled run: a white clock ring with a small gap at 12 o'clock and a play triangle inside,
  on a rounded square in the Busy blue (#2F6FED) of the app tokens. The tray icons are plain circles, so the
  square tile keeps the brand icon distinct from them. Stored as PNG entries at 16, 20, 24, 32, 40, 48, 64 and 256 px.
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

    # Clock ring: open at 12 o'clock (the gap), round caps. Thicker on small frames so it survives 16 px.
    $stroke = if ($size -le 24) { $size * 0.11 } else { $size * 0.085 }
    $radius = $size * 0.33
    $pen = New-Object System.Drawing.Pen $white, ([single]$stroke)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawArc($pen, [single]($c - $radius), [single]($c - $radius), [single](2 * $radius), [single](2 * $radius), [single]292, [single]316)

    # Play triangle, nudged right so its centroid (not its bounding box) sits at the center.
    $h = $size * 0.30
    $w = $size * 0.26
    $left = $c - ($w / 3.0)
    $points = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF([single]$left, [single]($c - $h / 2))),
        (New-Object System.Drawing.PointF([single]($left + $w), [single]$c)),
        (New-Object System.Drawing.PointF([single]$left, [single]($c + $h / 2))))
    $whiteBrush = New-Object System.Drawing.SolidBrush $white
    $g.FillPolygon($whiteBrush, $points)

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    if ($PreviewDirectory) {
        $null = New-Item -ItemType Directory -Force -Path $PreviewDirectory
        [System.IO.File]::WriteAllBytes((Join-Path (Resolve-Path $PreviewDirectory) "frame-$size.png"), $bytes)
    }

    $pen.Dispose(); $whiteBrush.Dispose(); $tile.Dispose(); $fill.Dispose(); $ms.Dispose(); $g.Dispose(); $bmp.Dispose()
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
