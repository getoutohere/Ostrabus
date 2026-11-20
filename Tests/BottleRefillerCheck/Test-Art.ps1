[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$assets=Join-Path $repo 'StandaloneMods/BottleRefiller/Assets'
$script:checks=0
function Assert([bool]$Value,[string]$Message){$script:checks++;if(-not $Value){throw $Message}}
$rows=Get-Content -LiteralPath (Join-Path $assets 'data/items/bottle_refiller.json') -Raw | ConvertFrom-Json
foreach($row in $rows){
    $file=Join-Path $assets ('images/'+$row.strImg+'.png')
    $image=[Drawing.Bitmap]::new($file)
    try{
        Assert ($image.Width -eq 16 -and $image.Height -eq 48) 'Non-native texture dimensions'
        $opaque=0;$outsideWall=0
        for($y=0;$y -lt 48;$y++){for($x=0;$x -lt 16;$x++){
            $pixel=$image.GetPixel($x,$y)
            Assert ($pixel.A -eq 0 -or $pixel.A -ge 128) 'Low-alpha fringe in production export'
            if($pixel.A -ge 128){$opaque++;if($y -ge 32){$outsideWall++}}
        }}
        Assert ($opaque -gt 100) 'Empty equipment texture'
        Assert ($outsideWall -ge 8) 'Output holder hidden inside middle wall tile'
        if(-not $row.strName.EndsWith('Loose')){
            Assert ($image.GetPixel(0,47).A -eq 0) 'Installed sprite has an opaque background'
        }
        Assert ($row.strImg.EndsWith('Loose') -eq $row.strName.EndsWith('Loose')) 'Loose packaging not selected by item definition'
    }finally{$image.Dispose()}
}
$installed=[Drawing.Bitmap]::new((Join-Path $assets 'images/ItmOstrabusBottleRefiller.png'))
$loose=[Drawing.Bitmap]::new((Join-Path $assets 'images/ItmOstrabusBottleRefillerLoose.png'))
try{
    $packing=0;$tan=0
    for($y=0;$y -lt 48;$y++){for($x=0;$x -lt 16;$x++){
        $a=$installed.GetPixel($x,$y);$b=$loose.GetPixel($x,$y)
        if($a.A -eq 0 -and $b.A -ge 128){$packing++}
        if($b.A -ge 128 -and $b.R -gt $b.G -and $b.G -gt $b.B+8){$tan++}
    }}
    Assert ($packing -ge 24 -and $tan -ge 24) 'Loose form has no visible tan packaging'
    # Use the visually approved artifacts as the reference. The pale native
    # lower part is equipment housing, not a packing cradle. Pixel counts
    # cannot establish visual quality; these checks prevent shipping a wrong
    # historical sprite after the artwork itself has been reviewed.
    $art=Join-Path $repo 'StandaloneMods/BottleRefiller/Art'
    Assert ((Get-FileHash -LiteralPath (Join-Path $assets 'images/ItmOstrabusBottleRefiller.png')).Hash -eq
        (Get-FileHash -LiteralPath (Join-Path $art 'bottle-refiller-undamaged-v9-16x48.png')).Hash) 'Installed asset differs from approved custom refiller'
    Assert ((Get-FileHash -LiteralPath (Join-Path $assets 'images/ItmOstrabusBottleRefillerLoose.png')).Hash -eq
        (Get-FileHash -LiteralPath (Join-Path $art 'bottle-refiller-loose-v12-16x48.png')).Hash) 'Loose asset differs from approved sleeve-wrapped custom refiller'
    $light=Get-Content -LiteralPath (Join-Path $assets 'data/lights/bottle_refiller.json') -Raw | ConvertFrom-Json
    $light=@($light)[0]
    $lightX=8+$light.ptPos.x;$lightY=24-$light.ptPos.y
    Assert ($light.strImg -eq 'ItmLitGreen' -and $light.strColor -eq 'Blank') 'Indicator differs from native pump or adds room lighting'
    Assert ($lightX -ge 0 -and $lightX -lt 16 -and $lightY -ge 32 -and $lightY -lt 48) 'Indicator is not on the exposed bottle side'
    Assert ($installed.GetPixel($lightX,$lightY).A -ge 128) 'Indicator floats off the equipment'
    $lampPixel=$installed.GetPixel($lightX,$lightY)
    Assert ($lampPixel.R -lt 65 -and $lampPixel.G -lt 65 -and $lampPixel.B -lt 65) 'Light misses the unlit lamp recess'
}finally{$installed.Dispose();$loose.Dispose()}
foreach($pair in @(
    @('ItmOstrabusBottleRefillerDmg','bottle-refiller-damaged-v1-16x48.png','ItmOstrabusBottleRefiller'),
    @('ItmOstrabusBottleRefillerDmgLoose','bottle-refiller-damaged-loose-v1-16x48.png','ItmOstrabusBottleRefillerLoose')
)){
    $damagedHash=(Get-FileHash -LiteralPath (Join-Path $assets ('images/'+$pair[0]+'.png'))).Hash
    Assert ($damagedHash -eq (Get-FileHash -LiteralPath (Join-Path $art $pair[1])).Hash) 'Damaged asset differs from its authored export'
    Assert ($damagedHash -ne (Get-FileHash -LiteralPath (Join-Path $assets ('images/'+$pair[2]+'.png'))).Hash) 'Damaged form still reuses healthy artwork'
}
Write-Host "$checks sprite checks passed (native grid, wall-tile boundary, packaging and alpha)."
