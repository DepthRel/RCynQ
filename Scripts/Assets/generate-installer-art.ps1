<#
.SYNOPSIS
    Draws the pictures of the Windows installer: the side panel of the welcome page and the small picture in the header
    of the other pages.

.DESCRIPTION
    The installer wizard is light, the RCynQ logo is made for the dark ground of the application. So the side panel is
    a piece of the application itself: the dark ground of its theme (Sources/Apps/RCynQ.UI/Themes/Colors.axaml), the
    violet and blue glow of the two halves of the logo, the logo and the word mark on it. The header of the other pages
    is white; there the logo stands alone.

    Files (BMP, which every version of Inno Setup reads), drawn at 2.5 times the size the wizard shows at 100%:
      rcynq-wizard.bmp         410 x 785   side panel
      rcynq-wizard-small.bmp   138 x 145   header picture

    App/build-installer.ps1 calls this script before compiling. Windows only (System.Drawing).

.PARAMETER OutputDirectory
    The target directory. Default: artifacts/installer-art.

.EXAMPLE
    ./Scripts/Assets/generate-installer-art.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$root = (Resolve-Path (Join-Path $scriptDir '..\..')).Path
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $root 'artifacts\installer-art'
}
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null

Add-Type -AssemblyName System.Drawing

function New-Color([string] $hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

# The palette of the application (Themes/Colors.axaml) and of the logo (Assets/rcynq.svg).
$ground = New-Color '#1B1D21'
$card = New-Color '#22252A'
$text = New-Color '#E6E8EB'
$muted = New-Color '#A0A6AE'
$violet = New-Color '#9333EA'
$violetLight = New-Color '#C084FC'
$blue = New-Color '#2563EB'
$blueLight = New-Color '#60A5FA'

function New-Canvas([int] $width, [int] $height) {
    $bitmap = New-Object System.Drawing.Bitmap $width, $height, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    return $bitmap, $graphics
}

# Draws a text centred at x; a word of several colours is drawn as pieces measured together.
function Write-Centered($graphics, [string[]] $parts, [System.Drawing.Color[]] $colors, $font, [single] $centerX, [single] $top) {
    $format = [System.Drawing.StringFormat]::GenericTypographic
    $widths = @($parts | ForEach-Object { $graphics.MeasureString($_, $font, 10000, $format).Width })
    $x = $centerX - (($widths | Measure-Object -Sum).Sum / 2)
    for ($i = 0; $i -lt $parts.Length; $i++) {
        $brush = New-Object System.Drawing.SolidBrush $colors[$i]
        $graphics.DrawString($parts[$i], $font, $brush, $x, $top, $format)
        $brush.Dispose()
        $x += $widths[$i]
    }
}

# A soft round glow of one colour, strongest in the centre.
function Add-Glow($graphics, [System.Drawing.Color] $color, [single] $centerX, [single] $centerY, [single] $size, [int] $alpha) {
    $bounds = New-Object System.Drawing.RectangleF ($centerX - $size / 2), ($centerY - $size / 2), $size, $size
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse($bounds)
    $brush = New-Object System.Drawing.Drawing2D.PathGradientBrush $path
    $brush.CenterColor = [System.Drawing.Color]::FromArgb($alpha, $color)
    $brush.SurroundColors = @([System.Drawing.Color]::FromArgb(0, $color))
    $graphics.FillEllipse($brush, $bounds)
    $brush.Dispose()
    $path.Dispose()
}

function Save-SidePanel([string] $logoPath, [string] $target) {
    $width = 410; $height = 785
    $bitmap, $graphics = New-Canvas $width $height
    try {
        # The ground of the application: the window colour turning into the card colour towards the lower right.
        $bounds = New-Object System.Drawing.Rectangle 0, 0, $width, $height
        $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush $bounds, $ground, $card, 60.0
        $graphics.FillRectangle($fill, $bounds)
        $fill.Dispose()

        # The two halves of the logo light the ground: violet from above, blue from below.
        $logoSize = 240
        $logoLeft = ($width - $logoSize) / 2
        $logoTop = 200
        $centerY = $logoTop + $logoSize / 2
        Add-Glow $graphics $violet ($width / 2 - 40) ($centerY - 70) 520 70
        Add-Glow $graphics $blue ($width / 2 + 40) ($centerY + 70) 520 60

        # The bar of the logo, drawn faintly across the whole panel: code moving from one side to the other.
        $lane = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $width, 0), ([System.Drawing.Color]::FromArgb(0, $blueLight)), ([System.Drawing.Color]::FromArgb(70, $blueLight))
        $graphics.FillRectangle($lane, 0, [single]($centerY - 3), $width, 6)
        $lane.Dispose()

        $logo = [System.Drawing.Image]::FromFile($logoPath)
        try {
            $graphics.DrawImage($logo, [single]$logoLeft, [single]$logoTop, [single]$logoSize, [single]$logoSize)
        }
        finally {
            $logo.Dispose()
        }

        # The word mark: R and C of "Remote Code" in blue, "yn" of "sYNchronization" in the text colour, Q of "Quick"
        # in violet.
        $wordFont = New-Object System.Drawing.Font 'Segoe UI', 50, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        Write-Centered $graphics @('RC', 'yn', 'Q') @($blueLight, $text, $violetLight) $wordFont ($width / 2) ($logoTop + $logoSize + 28)
        $wordFont.Dispose()

        $labelFont = New-Object System.Drawing.Font 'Consolas', 16, ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
        Write-Centered $graphics @('REMOTE CODE SYNC, QUICK') @($muted) $labelFont ($width / 2) ($logoTop + $logoSize + 104)
        $labelFont.Dispose()

        # A thin line where the panel meets the light page, blue turning into violet.
        $edge = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF 0, $height), $blue, $violet
        $graphics.FillRectangle($edge, $width - 3, 0, 3, $height)
        $edge.Dispose()

        $bitmap.Save($target, [System.Drawing.Imaging.ImageFormat]::Bmp)
    }
    finally {
        $graphics.Dispose(); $bitmap.Dispose()
    }
}

function Save-HeaderPicture([string] $logoPath, [string] $target) {
    $width = 138; $height = 145
    $bitmap, $graphics = New-Canvas $width $height
    try {
        $graphics.Clear([System.Drawing.Color]::White)
        $logo = [System.Drawing.Image]::FromFile($logoPath)
        try {
            $size = 122
            $graphics.DrawImage($logo, [single](($width - $size) / 2), [single](($height - $size) / 2), [single]$size, [single]$size)
        }
        finally {
            $logo.Dispose()
        }

        $bitmap.Save($target, [System.Drawing.Imaging.ImageFormat]::Bmp)
    }
    finally {
        $graphics.Dispose(); $bitmap.Dispose()
    }
}

$logoPath = Join-Path $root 'Sources\Apps\RCynQ.UI\Assets\rcynq-512.png'
if (-not (Test-Path $logoPath)) {
    throw "The logo is not found: $logoPath. Run Scripts/Assets/generate-icon.ps1 first."
}

Save-SidePanel $logoPath (Join-Path $OutputDirectory 'rcynq-wizard.bmp')
Save-HeaderPicture $logoPath (Join-Path $OutputDirectory 'rcynq-wizard-small.bmp')

Write-Host "Installer pictures: $OutputDirectory"
