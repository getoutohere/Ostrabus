[CmdletBinding()]
param([string]$GameDirectory=$env:OSTRANAUTS_DIR,
    [string]$PreviewDirectory=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../obj/BottleRefillerArt')))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot '../../../Build/Find-Game.ps1')
$GameDirectory=Find-Ostranauts -GameDirectory $GameDirectory
New-Item -ItemType Directory -Path $PreviewDirectory -Force | Out-Null

# Reuse the authored native wrapping, not the native equipment or generated art.
# Native OffLoose is precisely Off plus 378 pixels of one flat tan color.
$imagesPath=Join-Path $GameDirectory 'Ostranauts_Data/StreamingAssets/images'
$native=[Drawing.Bitmap]::new((Join-Path $imagesPath 'ItmAirPump02Off.png'))
$nativeLoose=[Drawing.Bitmap]::new((Join-Path $imagesPath 'ItmAirPump02OffLoose.png'))
$equipmentPath=Join-Path $PSScriptRoot 'bottle-refiller-undamaged-v9-16x48.png'
$expected='5AB8B3E9057E7EDABD787362499AEF20BD9DAD980EF93D7C336FEA220F70F953'
if((Get-FileHash -LiteralPath $equipmentPath).Hash -ne $expected){throw 'Custom refiller source changed; review it first.'}
$equipment=[Drawing.Bitmap]::new($equipmentPath)
$nativeLayer=[Drawing.Bitmap]::new(16,48,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
$sleeve=[Drawing.Bitmap]::new(16,48,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
$packed=[Drawing.Bitmap]::new(16,48,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
$comparison=[Drawing.Bitmap]::new(1168,864,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics=$null;$font=$null;$brush=$null
try{
    foreach($image in @($native,$nativeLoose,$equipment)){
        if($image.Width -ne 16 -or $image.Height -ne 48){throw 'Expected native 16x48 source textures.'}
    }
    $nativeChanges=0;$tanArgb=[Drawing.Color]::FromArgb(255,186,167,138).ToArgb()
    $clear=[Drawing.Color]::FromArgb(0,0,0,0)
    for($y=0;$y -lt 48;$y++){for($x=0;$x -lt 16;$x++){
        $a=$native.GetPixel($x,$y);$b=$nativeLoose.GetPixel($x,$y)
        # Ignore invisible RGB differences; every visible native difference
        # must be the authored tan sleeve, never machinery or pale housing.
        if($b.ToArgb() -ne $a.ToArgb() -and ($b.A -ne 0 -or $a.A -ne 0)){
            if($b.ToArgb() -ne $tanArgb){throw 'Native loose appearance contains more than the reviewed tan sleeve.'}
            $nativeLayer.SetPixel($x,$y,$b);$nativeChanges++
        }else{$nativeLayer.SetPixel($x,$y,$clear)}
    }}
    if($nativeChanges -ne 378){throw 'Native sleeve mask changed.'}

    # The custom device ends at row39, five pixels before the native row44.
    # Keep the sleeve's top and side geometry; translate its lower diagonal
    # fold and clipped bottom corners upwards five rows. Neither device is
    # scaled, repainted or moved. The empty lower five texture rows stay clear.
    $exposed=0;$covered=0;$mismatches=0
    for($y=0;$y -lt 48;$y++){for($x=0;$x -lt 16;$x++){
        $sourceY=if($y -lt 34){$y}else{$y+5}
        $wrap=if($sourceY -lt 48){$nativeLayer.GetPixel($x,$sourceY)}else{$clear}
        $sleeve.SetPixel($x,$y,$wrap)
        $device=$equipment.GetPixel($x,$y)
        if($wrap.A -gt 0){
            $packed.SetPixel($x,$y,$wrap)
            if($device.A -gt 0){$covered++}
        }else{
            $packed.SetPixel($x,$y,$device)
            if($device.A -gt 0){$exposed++}
        }
    }}
    $output=Join-Path $PSScriptRoot 'bottle-refiller-loose-v12-16x48.png'
    $packed.Save($output,[Drawing.Imaging.ImageFormat]::Png)
    $sleeve.Save((Join-Path $PSScriptRoot 'bottle-refiller-sleeve-v12-layer-16x48.png'),[Drawing.Imaging.ImageFormat]::Png)
    $nativeLayer.Save((Join-Path $PreviewDirectory 'native-pump-sleeve-layer-16x48.png'),[Drawing.Imaging.ImageFormat]::Png)
    $encoded=[Drawing.Bitmap]::new($output)
    try{
        for($y=0;$y -lt 48;$y++){for($x=0;$x -lt 16;$x++){
            $wrap=$sleeve.GetPixel($x,$y);$device=$equipment.GetPixel($x,$y)
            $expectedPixel=if($wrap.A -gt 0){$wrap}else{$device}
            if($encoded.GetPixel($x,$y).ToArgb() -ne $expectedPixel.ToArgb()){$mismatches++}
        }}
    }finally{$encoded.Dispose()}
    if($mismatches -ne 0 -or $exposed+$covered -ne 422){throw 'Export differs from the reviewed sleeve/source composition.'}

    # Technical comparison, each actual sprite enlarged exactly 16x; labels
    # and checkerboard remain outside the production-size export.
    $graphics=[Drawing.Graphics]::FromImage($comparison)
    $graphics.Clear([Drawing.Color]::FromArgb(255,24,29,34))
    $font=[Drawing.Font]::new('Segoe UI',12)
    $brush=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255,230,234,238))
    $panels=@($native,$nativeLoose,$equipment,$packed)
    $titles=@('Native pump: installed','Native pump: loose','Custom refiller: installed','Custom refiller: loose candidate')
    for($i=0;$i -lt 4;$i++){
        $left=24+$i*288
        $graphics.DrawString($titles[$i],$font,$brush,[single]$left,[single]14)
        for($y=0;$y -lt 768;$y++){for($x=0;$x -lt 256;$x++){
            $pixel=$panels[$i].GetPixel([int][Math]::Floor($x/16),[int][Math]::Floor($y/16))
            $shade=if(([int][Math]::Floor($x/32)+[int][Math]::Floor($y/32))%2){44}else{32}
            $red=[int][Math]::Round(($pixel.R*$pixel.A+$shade*(255-$pixel.A))/255)
            $green=[int][Math]::Round(($pixel.G*$pixel.A+$shade*(255-$pixel.A))/255)
            $blue=[int][Math]::Round(($pixel.B*$pixel.A+$shade*(255-$pixel.A))/255)
            $comparison.SetPixel($left+$x,48+$y,[Drawing.Color]::FromArgb(255,$red,$green,$blue))
        }}
    }
    $graphics.DrawString("Native tan sleeve overlaps the custom device; $exposed exposed equipment pixels unchanged, $covered covered by wrapping.",
        $font,$brush,[single]24,[single]832)
    $comparison.Save((Join-Path $PreviewDirectory 'bottle-refiller-loose-v12-comparison.png'),[Drawing.Imaging.ImageFormat]::Png)
    Write-Output "Native sleeve extracted: $nativeChanges tan pixels. Custom device: $exposed exposed, $covered covered; zero altered equipment pixels."
    Write-Output 'Art preview only; production assets, plugin and package unchanged.'
}finally{
    if($brush){$brush.Dispose()};if($font){$font.Dispose()};if($graphics){$graphics.Dispose()}
    $native.Dispose();$nativeLoose.Dispose();$equipment.Dispose();$nativeLayer.Dispose()
    $sleeve.Dispose();$packed.Dispose();$comparison.Dispose()
}
