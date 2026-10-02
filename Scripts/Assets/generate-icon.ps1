<#
.SYNOPSIS
    Renders the RCynQ application icon: a violet ring ("donut") crossed by a blue horizontal bar whose colors mix
    where they overlap.

.DESCRIPTION
    The master image is drawn at 1024x1024 and downscaled with bicubic filtering, which also anti-aliases the clipped
    overlap. Produces rcynq.ico (16-256 px, PNG-compressed frames), rcynq-256.png and rcynq-512.png. The vector
    original is Assets/rcynq.svg. Windows only (System.Drawing).

.PARAMETER OutputDirectory
    The target directory. Default: Sources/Apps/RCynQ.UI/Assets.
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = (Resolve-Path (Join-Path $scriptDir '..\..')).Path
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root 'Sources\Apps\RCynQ.UI\Assets'
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

function New-Color([string] $hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }
function New-Point([float] $x, [float] $y) { New-Object System.Drawing.PointF $x, $y }

$size = 1024
$master = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($master)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$g.Clear([System.Drawing.Color]::Transparent)

# Ring: centre 512, outer radius 390, inner radius 245 (the alternate fill mode leaves the hole empty).
$ring = New-Object System.Drawing.Drawing2D.GraphicsPath ([System.Drawing.Drawing2D.FillMode]::Alternate)
$ring.AddEllipse(122, 122, 780, 780)
$ring.AddEllipse(267, 267, 490, 490)

# Bar: pill from x=30 to x=994, height 200, centred vertically.
$bar = New-Object System.Drawing.Drawing2D.GraphicsPath
$bar.AddArc(30, 412, 200, 200, 90, 180)
$bar.AddArc(794, 412, 200, 200, 270, 180)
$bar.CloseFigure()

$violet = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Point 122 122), (New-Point 902 902), (New-Color '#D8B4FE'), (New-Color '#9333EA')
$blue = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Point 30 412), (New-Point 994 612), (New-Color '#60A5FA'), (New-Color '#2563EB')
$mix = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Point 122 412), (New-Point 902 612), (New-Color '#9094FB'), (New-Color '#5C4BEA')

$g.FillPath($violet, $ring)
$g.FillPath($blue, $bar)
$g.SetClip($ring)
$g.FillPath($mix, $bar)
$g.ResetClip()
$g.Dispose()

function Get-ScaledPng([System.Drawing.Bitmap] $source, [int] $target) {
    $bitmap = New-Object System.Drawing.Bitmap $target, $target, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.DrawImage($source, 0, 0, $target, $target)
    $graphics.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return , $stream.ToArray()
}

[System.IO.File]::WriteAllBytes((Join-Path $OutputDirectory 'rcynq-512.png'), (Get-ScaledPng $master 512))
[System.IO.File]::WriteAllBytes((Join-Path $OutputDirectory 'rcynq-256.png'), (Get-ScaledPng $master 256))

# ICO with PNG-compressed frames.
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$frames = foreach ($s in $sizes) { , (Get-ScaledPng $master $s) }
$ico = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $ico
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension)
    $writer.Write([byte]$dimension)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$frames[$i].Length)
    $writer.Write([UInt32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }
$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutputDirectory 'rcynq.ico'), $ico.ToArray())
$writer.Dispose()
$master.Dispose()

Write-Host "Icon written to $OutputDirectory"
