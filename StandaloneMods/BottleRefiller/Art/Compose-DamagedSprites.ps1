[CmdletBinding()]
param([string]$PreviewDirectory=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../obj/BottleRefillerArt')))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $PreviewDirectory -Force | Out-Null
$healthy=[Drawing.Bitmap]::new((Join-Path $PSScriptRoot 'bottle-refiller-undamaged-v9-16x48.png'))
$generated=[Drawing.Bitmap]::new((Join-Path $PSScriptRoot 'bottle-refiller-damaged-v1-export-16x48.png'))
$healthyLoose=[Drawing.Bitmap]::new((Join-Path $PSScriptRoot 'bottle-refiller-loose-v12-16x48.png'))
$sleevePath=Join-Path $PSScriptRoot 'bottle-refiller-sleeve-v12-layer-16x48.png'
if((Get-FileHash -LiteralPath $sleevePath).Hash -ne '69D955D4FBA532625091AB2890502871D3915AA156EF4BD463A078F96394CD7C'){
    throw 'The approved sleeve changed; review the source before composing damage artwork.'
}
$sleeve=[Drawing.Bitmap]::new($sleevePath)
$damaged=[Drawing.Bitmap]::new(16,48,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
$loose=[Drawing.Bitmap]::new(16,48,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
$comparison=[Drawing.Bitmap]::new(1168,864,[Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics=$null;$font=$null;$brush=$null
try{
    foreach($image in @($healthy,$generated,$healthyLoose,$sleeve)){
        if($image.Width -ne 16 -or $image.Height -ne 48){throw 'Expected native 16x48 inputs.'}
    }
    $clear=[Drawing.Color]::FromArgb(0,0,0,0)
    $changedTop=0;$changedBottom=0
    for($y=0;$y -lt 48;$y++){for($x=0;$x -lt 16;$x++){
        # Clip generated fringe/protrusions to the original equipment mask.
        # Damage can remove pixels, but cannot enlarge the mounting silhouette.
        $base=$healthy.GetPixel($x,$y)
        $pixel=if($base.A -gt 0){$generated.GetPixel($x,$y)}else{$clear}
        $damaged.SetPixel($x,$y,$pixel)
        if($base.ToArgb() -ne $pixel.ToArgb()){
            if($y -lt 16){$changedTop++}
            if($y -ge 32){$changedBottom++}
        }
        $wrap=$sleeve.GetPixel($x,$y)
        $loose.SetPixel($x,$y,$(if($wrap.A -gt 0){$wrap}else{$pixel}))
    }}
    if($changedTop -eq 0 -or $changedBottom -eq 0){throw 'Damage is confined to the hidden wall tile.'}
    $damaged.Save((Join-Path $PSScriptRoot 'bottle-refiller-damaged-v1-16x48.png'),[Drawing.Imaging.ImageFormat]::Png)
    $loose.Save((Join-Path $PSScriptRoot 'bottle-refiller-damaged-loose-v1-16x48.png'),[Drawing.Imaging.ImageFormat]::Png)

    $graphics=[Drawing.Graphics]::FromImage($comparison)
    $graphics.Clear([Drawing.Color]::FromArgb(255,24,29,34))
    $font=[Drawing.Font]::new('Segoe UI',12)
    $brush=[Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(255,230,234,238))
    $panels=@($healthy,$damaged,$healthyLoose,$loose)
    $titles=@('Healthy: installed','Damaged: installed','Healthy: loose','Damaged: loose')
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
    $graphics.DrawString('Actual 16x48 exports enlarged 16x. Damaged loose form uses the unchanged approved sleeve.',
        $font,$brush,[single]24,[single]832)
    $comparison.Save((Join-Path $PreviewDirectory 'bottle-refiller-damage-comparison.png'),[Drawing.Imaging.ImageFormat]::Png)
    Write-Output "Damage exports composed; $changedTop intake and $changedBottom bottle-side pixels differ. Approved healthy art and sleeve unchanged."
}finally{
    if($brush){$brush.Dispose()};if($font){$font.Dispose()};if($graphics){$graphics.Dispose()}
    $healthy.Dispose();$generated.Dispose();$healthyLoose.Dispose();$sleeve.Dispose()
    $damaged.Dispose();$loose.Dispose();$comparison.Dispose()
}
