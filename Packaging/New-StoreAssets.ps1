<#
.SYNOPSIS
  Generates the MSIX logo assets (all scale / targetsize variants) as PNGs.
  The artwork matches IconFactory.CreateAppIcon in TrayIcon.cs:
  purple->cyan gradient tile with white selection-corner brackets and a center dot.
#>
param([string]$OutDir = (Join-Path $PSScriptRoot 'Assets'))

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function Draw-AppIcon([System.Drawing.Graphics]$g, [float]$ox, [float]$oy, [float]$s) {
    # Rounded gradient tile
    $d = [float]($s * 0.24 * 2)
    $x = [float]($ox + 0.5); $y = [float]($oy + 0.5); $w = [float]($s - 1)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($x, $y, $d, $d, [float]180, [float]90)
    $path.AddArc([float]($x + $w - $d), $y, $d, $d, [float]270, [float]90)
    $path.AddArc([float]($x + $w - $d), [float]($y + $w - $d), $d, $d, [float]0, [float]90)
    $path.AddArc($x, [float]($y + $w - $d), $d, $d, [float]90, [float]90)
    $path.CloseFigure()
    $fill = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.RectangleF]::new($ox, $oy, $s, $s),
        [System.Drawing.Color]::FromArgb(139, 92, 246),
        [System.Drawing.Color]::FromArgb(6, 182, 212),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal)
    $g.FillPath($fill, $path)
    $fill.Dispose(); $path.Dispose()

    # Corner brackets
    $m = $s * 0.22; $len = $s * 0.2
    $l = $ox + $m; $t = $oy + $m; $r = $ox + $s - $m; $b = $oy + $s - $m
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, [float][Math]::Max(1.5, $s * 0.09))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'; $pen.LineJoin = 'Round'
    function P($px, $py) { [System.Drawing.PointF]::new([float]$px, [float]$py) }
    $g.DrawLines($pen, [System.Drawing.PointF[]]@((P $l ($t + $len)), (P $l $t), (P ($l + $len) $t)))
    $g.DrawLines($pen, [System.Drawing.PointF[]]@((P ($r - $len) $t), (P $r $t), (P $r ($t + $len))))
    $g.DrawLines($pen, [System.Drawing.PointF[]]@((P $r ($b - $len)), (P $r $b), (P ($r - $len) $b)))
    $g.DrawLines($pen, [System.Drawing.PointF[]]@((P ($l + $len) $b), (P $l $b), (P $l ($b - $len))))
    $pen.Dispose()

    # Center dot
    $dot = $s * 0.16
    $g.FillEllipse([System.Drawing.Brushes]::White,
        [float]($ox + ($s - $dot) / 2), [float]($oy + ($s - $dot) / 2), [float]$dot, [float]$dot)
}

# $Fraction = icon size relative to the shorter canvas side (rest is transparent padding).
function Save-Asset([int]$W, [int]$H, [double]$Fraction, [string]$FileName) {
    $bmp = [System.Drawing.Bitmap]::new($W, $H, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = [float]([Math]::Round([Math]::Min($W, $H) * $Fraction))
    Draw-AppIcon $g ([float][Math]::Round(($W - $s) / 2)) ([float][Math]::Round(($H - $s) / 2)) $s
    $g.Dispose()
    $bmp.Save((Join-Path $OutDir $FileName), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Get-ChildItem $OutDir -Filter *.png | Remove-Item

$scales = @{ 100 = 1.0; 125 = 1.25; 150 = 1.5; 200 = 2.0; 400 = 4.0 }
foreach ($scale in $scales.Keys) {
    $k = $scales[$scale]
    Save-Asset ([int](44 * $k))  ([int](44 * $k))  0.92 "Square44x44Logo.scale-$scale.png"
    Save-Asset ([int](150 * $k)) ([int](150 * $k)) 0.60 "Square150x150Logo.scale-$scale.png"
    Save-Asset ([int](310 * $k)) ([int](150 * $k)) 0.60 "Wide310x150Logo.scale-$scale.png"
    Save-Asset ([int](50 * $k))  ([int](50 * $k))  1.00 "StoreLogo.scale-$scale.png"
}

# Taskbar / Start / file-explorer icon sizes (plated and unplated).
foreach ($size in 16, 24, 32, 48, 256) {
    Save-Asset $size $size 0.92 "Square44x44Logo.targetsize-$size.png"
    Save-Asset $size $size 0.92 "Square44x44Logo.targetsize-${size}_altform-unplated.png"
}

Write-Host "Assets written to $OutDir"
