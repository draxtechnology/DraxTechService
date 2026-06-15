Add-Type -AssemblyName System.Drawing

$src = Join-Path $PSScriptRoot "..\Drax360NetworkManager\DraxLogo.png"
$original = [System.Drawing.Image]::FromFile((Resolve-Path $src))

# ── WiX dialog BMPs ──────────────────────────────────────────────────────────

function Save-Bmp {
    param($width, $height, $outName)
    $bmp = New-Object System.Drawing.Bitmap($width, $height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode    = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode  = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($original, 0, 0, $width, $height)
    $g.Dispose()
    $out = Join-Path $PSScriptRoot $outName
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $bmp.Dispose()
    Write-Host "Saved $out"
}

# WixUIBannerBmp  — top strip on all dialog headers
Save-Bmp 493 58  "banner.bmp"

# WixUIDialogBmp  — full background of the welcome/finish panel
Save-Bmp 493 312 "dialog.bmp"

# ── Multi-size ICO (16, 32, 48, 256) ─────────────────────────────────────────
# Each image is stored as a PNG chunk inside the ICO container (Vista+).
# Width/height byte = 0 encodes 256 in the ICO directory spec.

$sizes = @(16, 32, 48, 256)
$chunks = @()

foreach ($sz in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($sz, $sz)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($original, 0, 0, $sz, $sz)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $chunks += $ms
}

$icoPath = Join-Path $PSScriptRoot "..\Drax360NetworkManager\DraxLogo.ico"
$fs = [System.IO.File]::Create($icoPath)
$w  = New-Object System.IO.BinaryWriter($fs)

# ICONDIR header
$w.Write([uint16]0)                   # reserved
$w.Write([uint16]1)                   # type = ICO
$w.Write([uint16]$sizes.Count)        # image count

# Data starts after: 6-byte header + 16-byte entry × N
$offset = 6 + 16 * $sizes.Count

# ICONDIRENTRY for each size
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz   = $sizes[$i]
    $dim  = [byte]($sz -band 0xFF)    # 256 wraps to 0, which is correct per spec
    $w.Write($dim)                    # width
    $w.Write($dim)                    # height
    $w.Write([byte]0)                 # colour count (0 = not a palette image)
    $w.Write([byte]0)                 # reserved
    $w.Write([uint16]1)               # planes
    $w.Write([uint16]32)              # bits per pixel
    $w.Write([uint32]$chunks[$i].Length)
    $w.Write([uint32]$offset)
    $offset += $chunks[$i].Length
}

# Image data
foreach ($ms in $chunks) {
    $w.Write($ms.ToArray())
    $ms.Dispose()
}

$w.Dispose()
$fs.Dispose()
$original.Dispose()

Write-Host "Saved $icoPath"
Write-Host "Done."
