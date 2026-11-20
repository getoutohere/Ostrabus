param(
    [Parameter(Mandatory=$true)][string]$Source,
    [Parameter(Mandatory=$true)][string]$Destination,
    [string]$Preview,
    [ValidateRange(0.0,1.0)][double]$SampleX=0.5,
    [ValidateRange(0.0,1.0)][double]$SampleY=0.5
)

# Export the generated artwork without inventing or repainting pixels.
# Each native pixel samples its corresponding source cell. Optional fixed
# subcell offsets calibrate a generated grid whose details straddle the exact
# center; they never paint new pixels or smooth between source colors.
Add-Type -AssemblyName System.Drawing
if ([string]::IsNullOrWhiteSpace($Preview)) {
    $previewRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../obj/BottleRefillerArt'))
    New-Item -ItemType Directory -Path $previewRoot -Force | Out-Null
    $Preview=Join-Path $previewRoot ([IO.Path]::GetFileNameWithoutExtension($Destination)+'-preview.png')
}
$sourceImage = [System.Drawing.Bitmap]::new((Resolve-Path -LiteralPath $Source).Path)
$nativeImage = [System.Drawing.Bitmap]::new(16, 48, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$previewImage = [System.Drawing.Bitmap]::new(256, 768, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
try {
    for ($y = 0; $y -lt 48; $y++) {
        for ($x = 0; $x -lt 16; $x++) {
            $sourceX = [Math]::Min($sourceImage.Width-1,[int][Math]::Floor(($x + $SampleX) * $sourceImage.Width / 16))
            $sourceY = [Math]::Min($sourceImage.Height-1,[int][Math]::Floor(($y + $SampleY) * $sourceImage.Height / 48))
            $pixel = $sourceImage.GetPixel($sourceX, $sourceY)
            # Sprite cutout export: generated near-transparent matte pixels can
            # contain saturated RGB noise. They are not visible equipment.
            if ($pixel.A -lt 128) { $pixel = [System.Drawing.Color]::FromArgb(0, 0, 0, 0) }
            $nativeImage.SetPixel($x, $y, $pixel)
        }
    }
    $nativeImage.Save($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination), [System.Drawing.Imaging.ImageFormat]::Png)
    for ($y = 0; $y -lt 768; $y++) {
        for ($x = 0; $x -lt 256; $x++) {
            $nativeX = [int][Math]::Floor($x / 16)
            $nativeY = [int][Math]::Floor($y / 16)
            $pixel = $nativeImage.GetPixel($nativeX, $nativeY)
            $shade = if (([int][Math]::Floor($nativeX / 2) + [int][Math]::Floor($nativeY / 2)) % 2) { 44 } else { 32 }
            $red = [int][Math]::Round(($pixel.R * $pixel.A + $shade * (255 - $pixel.A)) / 255)
            $green = [int][Math]::Round(($pixel.G * $pixel.A + $shade * (255 - $pixel.A)) / 255)
            $blue = [int][Math]::Round(($pixel.B * $pixel.A + $shade * (255 - $pixel.A)) / 255)
            $previewImage.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(255, $red, $green, $blue))
        }
    }
    $previewImage.Save($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Preview), [System.Drawing.Imaging.ImageFormat]::Png)
} finally {
    $sourceImage.Dispose()
    $nativeImage.Dispose()
    $previewImage.Dispose()
}
