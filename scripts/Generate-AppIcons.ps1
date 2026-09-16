param(
    [string]$SourcePath = (Join-Path $PSScriptRoot '..\design\branding\app-icon.png'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\AdminConsoleFor1C.App\Assets')
)

# Windows-only asset conversion; preserves the approved artwork and its alpha.
# https://learn.microsoft.com/windows/apps/design/iconography/app-icon-construction
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$source = [System.Drawing.Bitmap]::new((Resolve-Path -LiteralPath $SourcePath).Path)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path

function Write-IconPng([string]$Name, [int]$Width, [int]$Height, [double]$Fill = 1.0) {
    $bitmap = [System.Drawing.Bitmap]::new($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $attributes = [System.Drawing.Imaging.ImageAttributes]::new()
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
        $ratio = [Math]::Min($Width / $sourceBounds.Width, $Height / $sourceBounds.Height) * $Fill
        $drawWidth = [Math]::Max(1, [int][Math]::Round($sourceBounds.Width * $ratio))
        $drawHeight = [Math]::Max(1, [int][Math]::Round($sourceBounds.Height * $ratio))
        $destination = [System.Drawing.Rectangle]::new(
            [int][Math]::Floor(($Width - $drawWidth) / 2),
            [int][Math]::Floor(($Height - $drawHeight) / 2), $drawWidth, $drawHeight)
        $graphics.DrawImage($source, $destination, $sourceBounds.X, $sourceBounds.Y,
            $sourceBounds.Width, $sourceBounds.Height, [System.Drawing.GraphicsUnit]::Pixel, $attributes)
        $bitmap.Save((Join-Path $OutputDirectory $Name), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $attributes.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

try {
    # Ignore stray, almost transparent pixels outside the visible silhouette.
    # Otherwise those pixels retain the original canvas padding at taskbar sizes.
    $left = $source.Width
    $top = $source.Height
    $right = -1
    $bottom = -1
    for ($y = 0; $y -lt $source.Height; $y++) {
        for ($x = 0; $x -lt $source.Width; $x++) {
            if ($source.GetPixel($x, $y).A -gt 8) {
                $left = [Math]::Min($left, $x)
                $top = [Math]::Min($top, $y)
                $right = [Math]::Max($right, $x)
                $bottom = [Math]::Max($bottom, $y)
            }
        }
    }
    if ($right -lt $left) { throw 'The source image is fully transparent.' }
    $sourceBounds = [System.Drawing.Rectangle]::FromLTRB($left, $top, $right + 1, $bottom + 1)

    $targetSizes = @(16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256)
    foreach ($size in $targetSizes) {
        $name = "Square44x44Logo.targetsize-$size.png"
        Write-IconPng $name $size $size
        foreach ($theme in @('unplated', 'lightunplated')) {
            Copy-Item -LiteralPath (Join-Path $OutputDirectory $name) `
                -Destination (Join-Path $OutputDirectory "Square44x44Logo.targetsize-${size}_altform-$theme.png")
        }
    }

    foreach ($scale in @(100, 125, 150, 200, 400)) {
        $factor = $scale / 100.0
        $appSize = [int][Math]::Ceiling(44 * $factor)
        $tileSize = [int][Math]::Ceiling(150 * $factor)
        $storeSize = [int][Math]::Ceiling(50 * $factor)
        Write-IconPng "Square44x44Logo.scale-$scale.png" $appSize $appSize
        Write-IconPng "Square150x150Logo.scale-$scale.png" $tileSize $tileSize
        Write-IconPng "StoreLogo.scale-$scale.png" $storeSize $storeSize
        Write-IconPng "Wide310x150Logo.scale-$scale.png" ([int][Math]::Ceiling(310 * $factor)) $tileSize
        Write-IconPng "SplashScreen.scale-$scale.png" ([int](620 * $factor)) ([int](300 * $factor)) 0.5
    }
    Write-IconPng 'StoreLogo.png' 50 50
    Write-IconPng 'LockScreenLogo.scale-200.png' 48 48

    # ICO directory followed by PNG-compressed frames, supported by Windows 11.
    $frames = @($targetSizes | ForEach-Object {
        [PSCustomObject]@{
            Size = $_
            Bytes = [IO.File]::ReadAllBytes((Join-Path $OutputDirectory "Square44x44Logo.targetsize-$_.png"))
        }
    })
    $iconPath = Join-Path $OutputDirectory 'AppIcon.ico'
    $writer = [IO.BinaryWriter]::new([IO.File]::Create($iconPath))
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$frames.Count)
        $offset = 6 + 16 * $frames.Count
        foreach ($frame in $frames) {
            $encodedSize = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$encodedSize)
            $writer.Write([byte]$encodedSize)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Bytes.Length)
            $writer.Write([uint32]$offset)
            $offset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
    }
    finally { $writer.Dispose() }

    Write-Output "Generated Windows icon assets in $OutputDirectory"
    Write-Output "ICO frames: $($targetSizes -join ', ')"
}
finally { $source.Dispose() }
