[CmdletBinding()]
param([string]$GameDirectory=$env:OSTRANAUTS_DIR)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\..\Build\Find-Game.ps1')
$game=Find-Ostranauts -GameDirectory $GameDirectory
$data=Join-Path $game 'Ostranauts_Data\StreamingAssets\data'
$assets=Join-Path $PSScriptRoot 'Assets'
function Read-Rows([string]$File) {
    $rows=Get-Content -LiteralPath (Join-Path $data $File) -Raw | ConvertFrom-Json
    foreach ($row in $rows) { $row }
}
function Clone($Value) { ConvertTo-Json -InputObject $Value -Depth 50 | ConvertFrom-Json }
function Save-Rows([string]$File,$Rows) {
    $path=Join-Path $assets $File
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $path) | Out-Null
    [IO.File]::WriteAllText($path,(ConvertTo-Json -InputObject @($Rows) -Depth 50)+"`n",[Text.UTF8Encoding]::new($false))
}
function Trigger([string]$Name,[string[]]$Requires,[string[]]$Forbids,[string[]]$Children=@()) {
    [ordered]@{strName=$Name;fChance=1.0;fCount=1.0;aReqs=@($Requires);aForbids=@($Forbids);aTriggers=@($Children);bAND=$true}
}
$id='ItmOstrabusApartmentRefuel'
$prefix='OstrabusApartmentRefuel'
$baseOwner=Read-Rows 'condowners\condowners.json' | Where-Object strName -eq 'ItmKiosk01'
$baseItem=Read-Rows 'items\items.json' | Where-Object strName -eq 'ItmKiosk01'
if ($null -eq $baseOwner -or $null -eq $baseItem) { throw 'Native kiosk definitions were not found.' }
$owners=@(); $items=@(); $jobs=@(); $interactions=@(); $loots=@(); $triggers=@()
$triggers+=Trigger "T${prefix}Ready" @() @() @('TNever')
$triggers+=Trigger "T${prefix}Working" @('IsOstrabusApartmentRefuel','IsInstalled') @('IsDamaged') @("T${prefix}Ready")
foreach ($state in @('','Loose','Dmg','DmgLoose')) {
    $loose=$state.EndsWith('Loose'); $damaged=$state.StartsWith('Dmg')
    $owner=Clone $baseOwner
    $owner.strName=$id+$state; $owner.strItemDef=$id+$state
    $owner.strNameShort='Residential Refuel Terminal'
    $owner.strNameFriendly='Residential Dockside Refuel Terminal'+$(if ($damaged) {' (Damaged)'} elseif ($loose) {' (Loose)'} else {''})
    $owner.strDesc='A private dockside connection to station fuel, life-support and electrical utilities. Install only in your owned station residence. Services directly docked vessels at normal station prices. The terminal contains no fuel. Uninstall before selling the residence to retain this expensive equipment.'
    $owner.aInteractions=@($(if ($loose) {'DropItem';'PickupItem'} elseif (-not $damaged) {'GUIOstrabusApartmentRefuel'}))
    $price=if ($loose) {if ($damaged) {50000} else {250000}} else {if ($damaged) {500} else {2500}}
    $conds=@('IsOstrabusApartmentRefuel=1.0x1','IsSalvageValueHigh=1.0x1','IsSolid=1.0x1','IsMechanical=1.0x1','IsCumbersome=1.0x1','IsOversized=1.0x1',
        'StatMass=1.0x31','StatDamageMax=1.0x70','StatInstallProgressMax=1.0x150','StatUninstallProgressMax=1.0x150',
        'StatRepairProgressMax=1.0x120','StatDismantleProgressMax=1.0x240',"StatBasePrice=1.0x$price")
    if (-not $loose) {$conds+=@('IsInstalled=1.0x1')}
    if ($damaged) {$conds+=@('IsDamaged=1.0x1')}
    $owner.aStartingConds=$conds
    $owner.mapGUIPropMaps=@($(if (-not $loose -and -not $damaged) {'Panel A';'OstrabusApartmentRefuel'}))
    $owner.mapPoints=@($(if (-not $loose) {'use,0,-24'}))
    $owner | Add-Member -Force NoteProperty mapSlotEffects @('drag','Blank')
    $destroy='MS'+$prefix+$state+'Destroy'
    $owner.aUpdateCommands=@("Destructable,StatDamage,$destroy,StatDamageMax,1.0")
    $owners+=@($owner)
    $item=Clone $baseItem; $item.strName=$id+$state
    if ($loose) {
        $item.fZScale=0.02; $item.aLights=@(); $item.aSocketAdds=@('TILItemAdds','TILItemAdds','TILItemAdds','TILItemAdds')
        $item.aSocketForbids=@('Blank','Blank','Blank','Blank','Blank','TILItemForbids','TILItemForbids','Blank','Blank','TILItemForbids','TILItemForbids','Blank','Blank','Blank','Blank','Blank')
        $item.aSocketReqs=@('Blank','Blank','Blank','Blank','Blank','TILFloor','TILFloor','Blank','Blank','TILFloor','TILFloor','Blank','Blank','Blank','Blank','Blank')
    }
    $items+=@($item)
    $requires=@('IsOstrabusApartmentRefuel'); $forbids=@()
    if ($loose) {$forbids+=@('IsInstalled')} else {$requires+=@('IsInstalled')}
    if ($damaged) {$requires+=@('IsDamaged')} else {$forbids+=@('IsDamaged')}
    $triggers+=Trigger "T${prefix}$state" $requires $forbids
    $out=if ($damaged) {'ItmTVDismantle'} else {'Output'+$prefix+$state+'Destroy'}
    if (-not $damaged) {
        $result=$id+$(if ($loose) {'DmgLoose'} else {'Dmg'})
        $loots+=@([ordered]@{strName=$out;strType='item';aCOs=@("$result=1.0x1");aLoots=@()})
    }
    $interactions+=@([ordered]@{strName=$destroy;strThemType='Self';fDuration=0.000001;bNoWalk=$true;bIgnoreFeelings=$true;CTTestUs='Blank';CTTestThem='Blank';objLootModeSwitch=$out})
    $loots+=@([ordered]@{strName=$destroy;strType='interaction';aCOs=@("$destroy=1.0x1");aLoots=@()})
}
$templates=Read-Rows 'installables\installables.json'
$repair=Read-Rows 'installables\installables_repair.json'
$restore=Read-Rows 'installables\installables_undamage.json'
$dismantle=Read-Rows 'installables\installables_dismantle.json'
foreach ($damage in @('','Dmg')) {
    $job=Clone ($templates | Where-Object strName -eq 'TV01OffInstall')
    $job.strName=$prefix+$damage+'Install'; $job.strActionCO=$id+$damage+'Loose'
    $job.CTThem="T${prefix}${damage}Loose"; $job.aInputs=@("T${prefix}${damage}Loose=1.0x1")
    $job.strStartInstall=$id+$damage; $job.aLootCOs=@($id+$damage)
    $job | Add-Member -Force NoteProperty CTUs "T${prefix}Ready"
    $job | Add-Member -Force NoteProperty CTAllowUs "T${prefix}Ready"
    $jobs+=@($job)
    $job=Clone ($templates | Where-Object strName -eq 'TV01Uninstall')
    $job.strName=$prefix+$damage+'Uninstall'; $job.strActionCO=$id+$damage
    $job.CTThem="T${prefix}${damage}"; $job.aLootCOs=@($id+$damage+'Loose')
    $jobs+=@($job)
}
foreach ($suffix in @('','Loose')) {
    $job=Clone ($repair | Where-Object strName -eq ('TV01Dmg'+$suffix+'Repair'))
    $job.strName=$prefix+'Dmg'+$suffix+'Repair'; $job.strActionCO=$id+'Dmg'+$suffix; $job.aLootCOs=@($id+$suffix)
    $jobs+=@($job)
    $job=Clone ($restore | Where-Object strName -eq 'TV01Undamage')
    $job.strName=$prefix+$suffix+'Undamage'; $job.strActionCO=$id+$suffix; $job.aLootCOs=@($id+$suffix)
    $jobs+=@($job)
}
foreach ($suffix in @('','Loose','Dmg','DmgLoose')) {
    $job=Clone ($dismantle | Where-Object strName -eq 'TV01LooseDismantle')
    $job.strName=$prefix+$suffix+'Dismantle'; $job.strActionCO=$id+$suffix
    $jobs+=@($job)
}
$use=Clone ((Read-Rows 'interactions\interactions.json') | Where-Object strName -eq 'GUIRefuelStation')
$use.strName='GUI'+$prefix; $use.CTTestThem="T${prefix}Working"; $use.aInverse=@('GUI'+$prefix+'Allow')
$use.strTooltip='Buy native station services for vessels directly docked to your owned residence.'
$interactions+=@($use)
$allow=Clone ((Read-Rows 'interactions\interactions.json') | Where-Object strName -eq 'GUIRefuelStationAllow')
$allow.strName='GUI'+$prefix+'Allow'; $allow.CTTestUs="T${prefix}Working"
$interactions+=@($allow)
$loots+=@([ordered]@{strName=$id+'Stock';strType='item';aCOs=@("${id}Loose=1.0x1");aLoots=@()})
Save-Rows 'mod_info.json' @([ordered]@{strName='Apartment Refuel Terminal';strAuthor='Jon';strGameVersion='1.1.0.0';strModVersion='0.1.1';strNotes='Requires the standalone ApartmentRefuel BepInEx plugin. Item definitions should remain installed for saves containing terminals.'})
Save-Rows 'data/condowners/apartment_refuel.json' $owners
Save-Rows 'data/items/apartment_refuel.json' $items
Save-Rows 'data/conditions_simple/apartment_refuel.json' @([ordered]@{strName='Apartment Refuel Conditions';aValues=@('IsOstrabusApartmentRefuel','Private Refuel Terminal','Residential station service terminal.','0','0','Neutral','false')})
Save-Rows 'data/condtrigs/apartment_refuel.json' $triggers
Save-Rows 'data/interactions/apartment_refuel.json' $interactions
Save-Rows 'data/installables/apartment_refuel.json' $jobs
Save-Rows 'data/loot/apartment_refuel.json' $loots
Save-Rows 'data/guipropmaps/apartment_refuel.json' @([ordered]@{strName='OstrabusApartmentRefuel';dictGUIPropMap=@('strGUIPrefab','GUIStationRefuel','strFriendlyName','Residential Refuel Terminal')})
Write-Host "Generated native apartment terminal definitions under $assets"
