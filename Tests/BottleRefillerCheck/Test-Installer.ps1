[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repo 'Build/Find-Game.ps1')
$nativeGame=Find-Ostranauts
$fixture=Join-Path $repo ('obj/BottleRefillerInstaller-'+[Guid]::NewGuid().ToString('N'))
$game=Join-Path $fixture 'Test Game';$mods=Join-Path $fixture 'Custom Mods';$settings=Join-Path $fixture 'settings.json'
$script:checks=0
function Assert([bool]$Value,[string]$Message){$script:checks++;if(-not $Value){throw $Message}}
function Json([string]$Path,$Value){[IO.File]::WriteAllText($Path,(ConvertTo-Json -InputObject @($Value) -Depth 64),[Text.UTF8Encoding]::new($false))}
# Only the process-presence check is a fixture boundary. File operations are real.
function Get-Process { [CmdletBinding()]param([string]$Name) }
foreach($folder in @('Ostranauts_Data/Managed','Ostranauts_Data/StreamingAssets/data/loot','BepInEx/core','BepInEx/plugins/Ostrabus')){
    New-Item -ItemType Directory -Path (Join-Path $game $folder) -Force | Out-Null
}
New-Item -ItemType Directory -Path $mods -Force | Out-Null
foreach($file in @('Ostranauts_Data/Managed/Assembly-CSharp.dll','BepInEx/core/BepInEx.dll')){
    Copy-Item -LiteralPath (Join-Path $nativeGame $file) -Destination (Join-Path $game $file)
}
Copy-Item -LiteralPath (Join-Path $nativeGame 'Ostranauts_Data/StreamingAssets/data/loot/loot.json') -Destination (Join-Path $game 'Ostranauts_Data/StreamingAssets/data/loot')
Json $settings @([ordered]@{strName='UserSettings';strPathMods=(Join-Path $mods 'placeholder.json')})
$orderPath=Join-Path $mods 'loading_order.json'
Json $orderPath @([ordered]@{strName='Mod Loading Order';aLoadOrder=@('core','OtherDisabled|disabled');aIgnorePatterns=@()})
$omni=Join-Path $game 'BepInEx/plugins/Ostrabus/Ostrabus.dll'
[IO.File]::WriteAllText($omni,'unrelated main mod fixture')
$omniHash=(Get-FileHash -LiteralPath $omni).Hash
$source=Join-Path $repo 'StandaloneMods/BottleRefiller'
$package=Join-Path $fixture 'Package'
& (Join-Path $source 'Package.ps1') -Destination $package -NoArchive -SkipTests | Out-Host
$dlls=@(Get-ChildItem -LiteralPath $package -Recurse -Filter '*.dll')
Assert ($dlls.Count -eq 1 -and $dlls[0].Name -eq 'Ostrabus.BottleRefiller.dll') 'Package redistributes other assemblies'
$install=Join-Path $package 'Install.ps1'
& $install -GameDirectory $game -SettingsPath $settings
$plugin=Join-Path $game 'BepInEx/plugins/OstrabusBottleRefiller/Ostrabus.BottleRefiller.dll'
$stockPath=Join-Path $mods 'OstrabusBottleRefiller/data/loot/supply_stock.json'
Assert (Test-Path -LiteralPath $plugin) 'Plugin not installed'
Assert ((Get-FileHash -LiteralPath $plugin).Hash -eq (Get-FileHash -LiteralPath $dlls[0].FullName).Hash) 'Wrong installed plugin bytes'
$stock=Get-Content -LiteralPath $stockPath -Raw | ConvertFrom-Json
Assert ($stock.Count -eq 18) 'Wrong Supply stock coverage'
foreach($row in $stock){Assert (@($row.aLoots | Where-Object {$_ -eq 'ItmOstrabusBottleRefillerStock=1.0x1'}).Count -eq 1) 'Missing or duplicate stock'}
$stockHash=(Get-FileHash -LiteralPath $stockPath).Hash
& $install -GameDirectory $game -SettingsPath $settings
Assert ((Get-FileHash -LiteralPath $stockPath).Hash -eq $stockHash) 'Reinstall changes stock'
$thirdRoot=Join-Path $mods 'ThirdParty/data/loot'
New-Item -ItemType Directory -Path $thirdRoot -Force | Out-Null
$third=$stock[0] | ConvertTo-Json -Depth 64 | ConvertFrom-Json
$third.aLoots=@('UniqueStock=1.0x1','ItmOstrabusBottleRefillerStock=1.0x1');$third.strType='item'
$thirdPath=Join-Path $thirdRoot 'stock.json'
[IO.File]::WriteAllText($thirdPath,"[/* comments supported by native loader */`n"+(ConvertTo-Json $third -Depth 64)+"`n]")
$order=Get-Content -LiteralPath $orderPath -Raw | ConvertFrom-Json
$order[0].aLoadOrder+='ThirdParty';Json $orderPath $order
& $install -GameDirectory $game -SettingsPath $settings
$stock=Get-Content -LiteralPath $stockPath -Raw | ConvertFrom-Json
$changed=$stock | Where-Object strName -eq $third.strName
Assert ($changed.aLoots -contains 'UniqueStock=1.0x1') 'Third-party goods lost'
Assert (@($changed.aLoots | Where-Object {$_ -eq 'ItmOstrabusBottleRefillerStock=1.0x1'}).Count -eq 1) 'Composition duplicated captured stock'
$order=Get-Content -LiteralPath $orderPath -Raw | ConvertFrom-Json
Assert ($order[0].aLoadOrder[-1] -eq 'OstrabusBottleRefiller' -and $order[0].aLoadOrder -contains 'OtherDisabled|disabled') 'Load order/state damaged'
$before=@{};foreach($path in @($plugin,$stockPath,$orderPath)){$before[$path]=(Get-FileHash -LiteralPath $path).Hash}
[IO.File]::WriteAllText($thirdPath,'[ invalid SupplyKiosk JSON ]')
$failed=$false;try{& $install -GameDirectory $game -SettingsPath $settings}catch{$failed=$true}
Assert $failed 'Malformed stock did not reject installation'
foreach($path in $before.Keys){Assert ((Get-FileHash -LiteralPath $path).Hash -eq $before[$path]) 'Failed installation did not roll back'}
[IO.File]::WriteAllText($thirdPath,(ConvertTo-Json -InputObject @($third) -Depth 64))
& $install -GameDirectory $game -SettingsPath $settings -Uninstall
Assert (-not (Test-Path -LiteralPath $plugin)) 'Plugin survived removal'
$stock=Get-Content -LiteralPath $stockPath -Raw | ConvertFrom-Json
Assert (@($stock | Where-Object {$_.aLoots -match '^ItmOstrabusBottleRefillerStock='}).Count -eq 0) 'Captured refiller stock survived removal'
Assert (($stock | Where-Object strName -eq $third.strName).aLoots -contains 'UniqueStock=1.0x1') 'Removal erased another mod stock'
Assert (Test-Path -LiteralPath (Join-Path $mods 'OstrabusBottleRefiller/data/condowners/bottle_refiller.json')) 'Saved item definitions removed'
Assert (Test-Path -LiteralPath (Join-Path $mods 'OstrabusBottleRefiller/images/ItmOstrabusBottleRefiller.png')) 'Saved item image removed'
& $install -GameDirectory $game -SettingsPath $settings
Assert (Test-Path -LiteralPath $plugin) 'Reinstall after removal failed'
Assert ((Get-FileHash -LiteralPath $omni).Hash -eq $omniHash) 'Main Ostrabus plugin modified'
$assembly=Join-Path $game 'Ostranauts_Data/Managed/Assembly-CSharp.dll'
$beforeAssembly=(Get-FileHash -LiteralPath $assembly).Hash
# A trailing byte changes the file hash without changing its managed metadata
# or the AddCommand API. Installation must accept this valid assembly copy.
$stream=[IO.File]::Open($assembly,[IO.FileMode]::Append,[IO.FileAccess]::Write)
try{$stream.WriteByte(0)}finally{$stream.Dispose()}
Assert ((Get-FileHash -LiteralPath $assembly).Hash -ne $beforeAssembly) 'Fixture did not change the game assembly hash'
Assert ([Reflection.AssemblyName]::GetAssemblyName($assembly).Name -eq 'Assembly-CSharp') 'Changed-hash fixture is not a valid managed game assembly'
$beforeOrder=(Get-FileHash -LiteralPath $orderPath).Hash
& $install -GameDirectory $game -SettingsPath $settings
Assert ((Get-FileHash -LiteralPath $orderPath).Hash -eq $beforeOrder) 'Changed-hash install altered native order'
Write-Host "$checks installer checks passed. Fixture: $fixture"
