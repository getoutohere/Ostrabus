[CmdletBinding()]
param([string]$GameDirectory=$env:OSTRANAUTS_DIR)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../../Build/Find-Game.ps1')
$game=Find-Ostranauts -GameDirectory $GameDirectory
$data=Join-Path $game 'Ostranauts_Data/StreamingAssets/data'
$assets=Join-Path $PSScriptRoot 'Assets'
function Read-Rows([string]$File) {
    $rows=Get-Content -LiteralPath (Join-Path $data $File) -Raw | ConvertFrom-Json
    foreach($row in $rows){$row}
}
function Clone($Value) {if($null -eq $Value){throw 'Missing donor definition'}; ConvertTo-Json -InputObject $Value -Depth 64 | ConvertFrom-Json}
function Set-Value($Row,[string]$Name,$Value){$Row | Add-Member -Force NoteProperty $Name $Value}
function Save-Rows([string]$File,$Rows){
    $path=Join-Path $assets $File
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    [IO.File]::WriteAllText($path,(ConvertTo-Json -InputObject @($Rows) -Depth 64)+"`n",[Text.UTF8Encoding]::new($false))
}
function Trigger([string]$Name,[string[]]$Requires,[string[]]$Forbids){
    [ordered]@{strName=$Name;fChance=1.0;fCount=1.0;aReqs=@($Requires);aForbids=@($Forbids);aTriggers=@();bAND=$true}
}
$prefix='OstrabusBottleRefiller'; $id='Itm'+$prefix
$nativeOwners=@(Read-Rows 'condowners/condowners.json'); $nativeItems=@(Read-Rows 'items/items.json')
$owners=@();$items=@();$triggers=@();$interactions=@();$loots=@();$jobs=@()
foreach($state in @('','Loose','Dmg','DmgLoose')){
    $loose=$state.EndsWith('Loose');$damaged=$state.StartsWith('Dmg')
    $donor=if($damaged){'ItmAirPump02'+$state}else{'ItmAirPump02Off'+$state}
    $owner=Clone ($nativeOwners | Where-Object strName -eq $donor)
    $owner.strName=$id+$state; $owner.strItemDef=$id+$state
    $owner.strNameShort='Bottle Refiller'
    $owner.strNameFriendly='Wall-mounted Bottle Refiller'+$(if($damaged){' (Damaged)'}elseif($loose){' (Loose)'}else{''})
    $owner.strDesc='A powered wall-mounted four-bottle filling manifold. ON tops up undamaged handheld O2 bottles to their rated pressure using a gas canister at the intake, or cabin gas if no canister is present. Transfers the entire gas mixture: this is NOT an oxygen filter. An empty intake canister stops filling. Install on a wall and supply electrical power.'
    $owner.aStartingConds=@($owner.aStartingConds | Where-Object {$_ -notmatch '^(IsHiddenInv|IsAirPump|IsSignalable|IsOff|IsReverse|IsSlowMode|IsTurbo|IsPowered|IsOverrideOn|IsOverrideOff)='})+@('IsOstrabusBottleRefiller=1.0x1','IsOverrideOff=1.0x1')
    if(-not $loose){$owner.aStartingConds+=@('IsHiddenInv=1.0x1')}
    $owner.aInteractions=@('Inventory')+@($(if($loose){'DropItem';'PickupItem'}elseif(-not $damaged){'GUI'+$prefix}))
    if(-not $loose){$owner.aInteractions+=@('ACT'+$prefix+$state+'Repack')}
    # Retain native drag metadata for reconstructing 0.1.0 saves that already
    # carry a bad installed instance. The component clears runtime drag
    # eligibility on installed world objects after reconstruction.
    $owner.mapSlotEffects=@('drag','Blank')
    $owner.mapGUIPropMaps=@($(if(-not $loose -and -not $damaged){'Panel A';$prefix}))
    $owner.mapPoints=@('use,0,0','GasInput,0,16')
    if(-not $loose){$owner.mapPoints+=@('PowerA,0,0')}
    $owner.strPortraitImg=$id+$state
    Set-Value $owner 'strContainerCT' 'TIsFitContainerEVABottleNotDamaged'
    Set-Value $owner 'nContainerWidth' 2;Set-Value $owner 'nContainerHeight' 2
    Set-Value $owner 'strLoot' $null
    Set-Value $owner 'strAudioEmitter' $null
    Set-Value $owner 'aTickers' @($(if(-not $loose -and -not $damaged){'Power'}))
    Set-Value $owner 'jsonPI' $(if(-not $loose -and -not $damaged){$prefix}else{$null})
    $destroy='MS'+$prefix+$state+'Damage'
    $owner.aUpdateCommands=@($prefix,"Destructable,StatDamage,$destroy,StatDamageMax,1.0")
    $owners+=@($owner)
    $item=Clone ($nativeItems | Where-Object strName -eq $donor)
    $item.strName=$id+$state;$item.strImg=$id+$state
    # A neutral normal avoids imposing the old pump's different coupler shape.
    $item.strImgNorm=$id+'n';$item.strImgDamaged='blank'
    Set-Value $item 'aLights' @($(if(-not $loose -and -not $damaged){$prefix+'Filling';$prefix+'Standby'}))
    $items+=@($item)
    $requires=@('IsOstrabusBottleRefiller');$forbids=@()
    if($loose){$forbids+='IsInstalled'}else{$requires+='IsInstalled'}
    if($damaged){$requires+='IsDamaged'}else{$forbids+='IsDamaged'}
    $triggers+=Trigger "T${prefix}$state" $requires $forbids
    if(-not $loose){
        $carried=Trigger "T${prefix}${state}Carried" $requires $forbids
        $carried.aTriggers=@('TIsCarried')
        $triggers+=@($carried)
        $pack='ACT'+$prefix+$state+'Repack'
        $output='Output'+$pack
        $loots+=@([ordered]@{strName=$output;strType='item';bSuppress=$true;aCOs=@(($id+$state+'Loose=1.0x1'));aLoots=@()})
        $interactions+=@([ordered]@{
            strName=$pack;strTitle='Repack carried refiller';strDesc='[us] [repackages] [them].';strTooltip='Recover a refiller incorrectly carried while installed by version 0.1.0.'
            strThemType='Other';strActionGroup='Use';strTargetPoint='use';fTargetPointRange=1;fDuration=0.000027;bNoWalk=$true;bOpener=$true;bIgnoreFeelings=$true
            CTTestUs='TIsHuman';CTTestThem="T${prefix}${state}Carried";aInverse=@($pack+'Allow')
        })
        $interactions+=@([ordered]@{
            strName=$pack+'Allow';strThemType='Other';fDuration=0.000001;bNoWalk=$true;bIgnoreFeelings=$true
            CTTestUs="T${prefix}${state}Carried";CTTestThem='TIsHuman';objLootModeSwitch=$output
        })
    }
    # Damaged units dismantle normally; healthy units convert, preserving contents.
    $out=if($damaged){'ItmAirPumpDismantle'}else{'Output'+$destroy}
    if(-not $damaged){
        $result=$id+$(if($loose){'DmgLoose'}else{'Dmg'})
        $loots+=@([ordered]@{strName=$out;strType='item';bSuppress=$true;aCOs=@("$result=1.0x1");aLoots=@()})
    }
    $interactions+=@([ordered]@{strName=$destroy;strThemType='Self';fDuration=0.000001;bNoWalk=$true;bIgnoreFeelings=$true;CTTestUs='Blank';CTTestThem='Blank';objLootModeSwitch=$out})
    $loots+=@([ordered]@{strName=$destroy;strType='interaction';aCOs=@("$destroy=1.0x1");aLoots=@()})
}
$triggers+=Trigger "T${prefix}UsePower" @('IsOstrabusBottleRefiller','IsInstalled','IsOverrideOn') @('IsDamaged','IsOverrideOff')
$triggers+=Trigger "T${prefix}Panel" @('IsOstrabusBottleRefiller','IsInstalled') @('IsDamaged')
$nativeJobs=@(Read-Rows 'installables/installables.json')
foreach($damage in @('','Dmg')){
    $job=Clone ($nativeJobs | Where-Object strName -eq $(if($damage){'AirPump02DmgInstall'}else{'AirPump02Install'}))
    $job.strName=$prefix+$damage+'Install';$job.strActionCO=$id+$damage+'Loose'
    $job.CTThem="T${prefix}${damage}Loose";$job.aInputs=@("T${prefix}${damage}Loose=1.0x1")
    $job.aLootCOs=@($id+$damage);$job.strStartInstall=$id+$damage;$jobs+=@($job)
    $job=Clone ($nativeJobs | Where-Object strName -eq $(if($damage){'AirPump02DmgUninstall'}else{'AirPump02OffUninstall'}))
    $job.strName=$prefix+$damage+'Uninstall';$job.strActionCO=$id+$damage
    $job.CTThem="T${prefix}${damage}";$job.aLootCOs=@($id+$damage+'Loose');$jobs+=@($job)
}
$repair=@(Read-Rows 'installables/installables_repair.json');$undamage=@(Read-Rows 'installables/installables_undamage.json');$dismantle=@(Read-Rows 'installables/installables_dismantle.json')
foreach($suffix in @('','Loose')){
    $job=Clone ($repair | Where-Object strName -eq ('AirPump02Dmg'+$suffix+'Repair'))
    $job.strName=$prefix+'Dmg'+$suffix+'Repair';$job.strActionCO=$id+'Dmg'+$suffix
    $job.CTThem="T${prefix}Dmg${suffix}";$job.aLootCOs=@($id+$suffix);$jobs+=@($job)
    $job=Clone ($undamage | Where-Object strName -eq ('AirPump02Off'+$suffix+'Undamage'))
    $job.strName=$prefix+$suffix+'Undamage';$job.strActionCO=$id+$suffix;$job.aLootCOs=@($id+$suffix);$jobs+=@($job)
}
foreach($suffix in @('','Loose','Dmg','DmgLoose')){
    $job=Clone ($dismantle | Where-Object strName -eq 'AirPump02OffLooseDismantle')
    $job.strName=$prefix+$suffix+'Dismantle';$job.strActionCO=$id+$suffix;$jobs+=@($job)
}
# Predefine generated conversion outputs so Installables.Create uses suppression.
foreach($job in $jobs){if($job.aLootCOs){$loots+=@([ordered]@{strName='Output'+$job.strName;strType='item';bSuppress=$true;aCOs=@($job.aLootCOs | ForEach-Object {"$_=1.0x1"});aLoots=@()})}}
$nativeInteractions=@(Read-Rows 'interactions/interactions.json')
$use=Clone ($nativeInteractions | Where-Object strName -eq 'GUIAirPump')
$use.strName='GUI'+$prefix;$use.CTTestThem="T${prefix}Panel";$use.aInverse=@('GUI'+$prefix+'Allow')
$use.strTooltip='Switch automatic bottle filling OFF or ON.';$interactions+=@($use)
$allow=Clone ($nativeInteractions | Where-Object strName -eq 'GUIAirPumpAllow')
$allow.strName='GUI'+$prefix+'Allow';$allow.CTTestUs="T${prefix}Panel";$interactions+=@($allow)
# Native Powered sets/clears IsPowered itself; no mode conversion or event hook.
$interactions+=@([ordered]@{strName=$prefix+'PowerChanged';strThemType='Self';CTTestUs='TNever';CTTestThem='TNever';bNoWalk=$true;fDuration=0.000001})
$power=Clone ((Read-Rows 'powerinfos/powerinfos.json') | Where-Object strName -eq 'AirPump02')
$power.strName=$prefix;$power.strUsePowerCT="T${prefix}UsePower"
$power.strIntPowerOn=$prefix+'PowerChanged';$power.strIntPowerOff=$prefix+'PowerChanged'
$power.strShutDownCT='TNever';$power.strOverrideCond=$null;$power.fOverrideAmount=0
$loots+=@([ordered]@{strName=$id+'Stock';strType='item';aCOs=@("${id}Loose=1.0x1");aLoots=@()})
Save-Rows 'mod_info.json' @([ordered]@{strName='Bottle Refiller';strAuthor='Jon';strGameVersion='1.1.0.4';strModVersion='0.1.7';strNotes='Requires the standalone Bottle Refiller BepInEx plugin. Retain item definitions for existing saves.'})
Save-Rows 'data/condowners/bottle_refiller.json' $owners
Save-Rows 'data/items/bottle_refiller.json' $items
# Native pump indicator, aligned to the black recess in this refiller's
# bottle-side face: (3,-13) maps to image pixel (11,37). The face is two
# pixels shallower than the donor pump's (3,-15) lamp. Both are outside
# the middle wall tile, on the output side. Native Item handles rotation.
$light=Clone ((Read-Rows 'lights/lights.json') | Where-Object strName -eq 'AirPumpRGG')
$light.strName=$prefix+'Filling';$light.ptPos.y=-13
$standby=Clone $light;$standby.strName=$prefix+'Standby';$standby.strImg='ItmLitYellow'
Save-Rows 'data/lights/bottle_refiller.json' @($light,$standby)
Save-Rows 'data/conditions_simple/bottle_refiller.json' @([ordered]@{strName='Bottle Refiller Conditions';aValues=@('IsOstrabusBottleRefiller','Bottle Refiller','Wall-mounted bottle filling manifold.','0','0','Neutral','false')})
Save-Rows 'data/condtrigs/bottle_refiller.json' $triggers
Save-Rows 'data/interactions/bottle_refiller.json' $interactions
Save-Rows 'data/installables/bottle_refiller.json' $jobs
Save-Rows 'data/loot/bottle_refiller.json' $loots
Save-Rows 'data/guipropmaps/bottle_refiller.json' @([ordered]@{strName=$prefix;dictGUIPropMap=@('strGUIPrefab','GUIOnOff','strFriendlyName','Bottle Refiller','strTitle','Bottle Refiller')})
Save-Rows 'data/powerinfos/bottle_refiller.json' @($power)
New-Item -ItemType Directory -Path (Join-Path $assets 'images') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Art/bottle-refiller-undamaged-v9-16x48.png') -Destination (Join-Path $assets "images/$id.png") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Art/bottle-refiller-loose-v12-16x48.png') -Destination (Join-Path $assets "images/${id}Loose.png") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Art/bottle-refiller-damaged-v1-16x48.png') -Destination (Join-Path $assets "images/${id}Dmg.png") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Art/bottle-refiller-damaged-loose-v1-16x48.png') -Destination (Join-Path $assets "images/${id}DmgLoose.png") -Force
# A normal map is technical vector data, not a second generated illustration.
Add-Type -AssemblyName System.Drawing
$normal=[Drawing.Bitmap]::new(16,48)
try{
    for($y=0;$y -lt 48;$y++){for($x=0;$x -lt 16;$x++){$normal.SetPixel($x,$y,[Drawing.Color]::FromArgb(255,128,128,255))}}
    $normal.Save((Join-Path $assets "images/${id}n.png"),[Drawing.Imaging.ImageFormat]::Png)
}finally{$normal.Dispose()}
Write-Host 'Generated Bottle Refiller native definitions and 16x48 textures.'
