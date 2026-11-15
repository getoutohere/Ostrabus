[CmdletBinding()]
param([string]$GameDirectory=$env:OSTRANAUTS_DIR, [switch]$WithCustomInstallers)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repo 'Build\Find-Game.ps1')
$nativeGame = Find-Ostranauts -GameDirectory $GameDirectory
$fixture = Join-Path $repo ('obj\ApartmentRefuel\installer-' + [Guid]::NewGuid().ToString('N'))
$game = Join-Path $fixture 'Test Game'
$mods = Join-Path $fixture 'Custom Mods'
$settings = Join-Path $fixture 'settings.json'
$checks = 0
function Assert([bool]$Value,[string]$Message) { $script:checks++; if (-not $Value) { throw $Message } }
function Json([string]$Path,$Value) { [IO.File]::WriteAllText($Path,(ConvertTo-Json -InputObject @($Value | ForEach-Object { $_ }) -Depth 64),[Text.UTF8Encoding]::new($false)) }
# Only the process check is a fixture boundary; all file installation is real.
function Get-Process { [CmdletBinding()]param([string]$Name) }
foreach ($folder in @('Ostranauts_Data\Managed','Ostranauts_Data\StreamingAssets\data\loot','BepInEx\core','BepInEx\plugins\Ostrabus')) {
    New-Item -ItemType Directory -Path (Join-Path $game $folder) -Force | Out-Null
}
New-Item -ItemType Directory -Path $mods -Force | Out-Null
foreach ($file in @('Ostranauts_Data\Managed\Assembly-CSharp.dll','BepInEx\core\BepInEx.dll')) {
    Copy-Item -LiteralPath (Join-Path $nativeGame $file) -Destination (Join-Path $game $file)
}
Copy-Item -LiteralPath (Join-Path $nativeGame 'Ostranauts_Data\StreamingAssets\data\loot\loot.json') -Destination (Join-Path $game 'Ostranauts_Data\StreamingAssets\data\loot')
Json $settings @([ordered]@{strName='UserSettings';strPathMods=(Join-Path $mods 'placeholder.json')})
$orderPath = Join-Path $mods 'loading_order.json'
Json $orderPath @([ordered]@{strName='Mod Loading Order';aLoadOrder=@('core','Unrelated|disabled');aIgnorePatterns=@()})
$omni = Join-Path $game 'BepInEx\plugins\Ostrabus\Ostrabus.dll'
[IO.File]::WriteAllText($omni,'unrelated omnibus fixture')
$omniHash = (Get-FileHash -LiteralPath $omni).Hash
$source = Join-Path $repo 'StandaloneMods\ApartmentRefuel'
$package = Join-Path $fixture 'Package'
& (Join-Path $source 'Package.ps1') -Destination $package -NoArchive | Out-Host
$dlls = @(Get-ChildItem -LiteralPath $package -Recurse -Filter '*.dll')
Assert ($dlls.Count -eq 1 -and $dlls[0].Name -eq 'Ostrabus.ApartmentRefuel.dll') 'Package redistributes other assemblies'
$install = Join-Path $package 'Install.ps1'
$compose = Join-Path $repo 'Build\Update-ApartmentRefuelStock.ps1'
& $install -GameDirectory $game -SettingsPath $settings
$stockFile = Join-Path $mods 'OstrabusApartmentRefuel\data\loot\supply_stock.json'
$plugin = Join-Path $game 'BepInEx\plugins\OstrabusApartmentRefuel\Ostrabus.ApartmentRefuel.dll'
Assert (Test-Path -LiteralPath $plugin) 'Standalone DLL not installed'
Assert ((Get-FileHash -LiteralPath $plugin).Hash -eq (Get-FileHash -LiteralPath $dlls[0].FullName).Hash) 'Wrong standalone DLL installed'
$stock = Get-Content -LiteralPath $stockFile -Raw | ConvertFrom-Json
Assert ($stock.Count -eq 17) 'Wrong stock count'
foreach ($row in $stock) { Assert (@($row.aLoots | Where-Object { $_ -eq 'ItmOstrabusApartmentRefuelStock=1.0x1' }).Count -eq 1) 'Duplicate/missing terminal stock' }
$stockHash = (Get-FileHash -LiteralPath $stockFile).Hash
& $install -GameDirectory $game -SettingsPath $settings
Assert ((Get-FileHash -LiteralPath $stockFile).Hash -eq $stockHash) 'Repeated install changes stock'
$rotation = Join-Path $repo 'NavConsole\Rotation\Assets'
& (Join-Path $repo 'Build\Install-NativeMod.ps1') -ModName OstrabusRotation -AssetSource $rotation -GameDirectory $game -SettingsPath $settings -Enable
& $compose -GameDirectory $game -SettingsPath $settings
$stock = Get-Content -LiteralPath $stockFile -Raw | ConvertFrom-Json
$rotationRows = Get-Content -LiteralPath (Join-Path $rotation 'data\loot\rotation_stock.json') -Raw | ConvertFrom-Json
foreach ($row in $stock) {
    $expectedRotation = @($rotationRows | Where-Object strName -eq $row.strName).Count
    Assert (@($row.aLoots | Where-Object { $_ -match 'OstrabusNavRotation' }).Count -eq $expectedRotation) 'Rotation stock erased by terminal mod'
}
$order = Get-Content -LiteralPath $orderPath -Raw | ConvertFrom-Json
Assert ($order[0].aLoadOrder[-1] -eq 'OstrabusApartmentRefuel' -and $order[0].aLoadOrder -contains 'Unrelated|disabled') 'Composer damaged native mod order/states'
& (Join-Path $repo 'Build\Install-NativeMod.ps1') -ModName OstrabusRotation -GameDirectory $game -SettingsPath $settings -Disabled -RetainDefinitions -OmissionFiles @('data/loot/rotation_stock.json')
& $compose -GameDirectory $game -SettingsPath $settings
$stock = Get-Content -LiteralPath $stockFile -Raw | ConvertFrom-Json
Assert (@($stock | Where-Object { $_.aLoots -match 'OstrabusNavRotation' }).Count -eq 0) 'Omitted rotation stock persists'
Assert ((Get-FileHash -LiteralPath $plugin).Hash -eq (Get-FileHash -LiteralPath $dlls[0].FullName).Hash) 'Profile change touched standalone DLL'
# A commented third-party stock override must retain all its authored fields.
$thirdRoot = Join-Path $mods 'ThirdParty\data\loot'
New-Item -ItemType Directory -Path $thirdRoot -Force | Out-Null
$third = $stock[0] | ConvertTo-Json -Depth 30 | ConvertFrom-Json
$third.aLoots = @('UniqueThirdPartyStock=1.0x1'); $third.strType = 'item'
[IO.File]::WriteAllText((Join-Path $thirdRoot 'stock.json'),"[/* native JSON comments */`n"+(ConvertTo-Json $third -Depth 30)+"`n]")
$order[0].aLoadOrder += 'ThirdParty'; Json $orderPath $order
& $compose -GameDirectory $game -SettingsPath $settings
$stock = Get-Content -LiteralPath $stockFile -Raw | ConvertFrom-Json
$changed = $stock | Where-Object strName -eq $third.strName
Assert ($changed.aLoots -contains 'UniqueThirdPartyStock=1.0x1' -and $changed.aLoots -contains 'ItmOstrabusApartmentRefuelStock=1.0x1') 'Third party stock lost'
$beforeBad = (Get-FileHash -LiteralPath $stockFile).Hash
[IO.File]::WriteAllText((Join-Path $thirdRoot 'stock.json'),'[ invalid ItmSupplyKiosk JSON ]')
$failed = $false
try { & $compose -GameDirectory $game -SettingsPath $settings } catch { $failed = $true }
Assert ($failed -and (Get-FileHash -LiteralPath $stockFile).Hash -eq $beforeBad) 'Malformed source mutated composed stock'
# Ignore patterns follow the native sanitized full-path match.
$order = Get-Content -LiteralPath $orderPath -Raw | ConvertFrom-Json
$order[0].aIgnorePatterns = @('ThirdParty/'); Json $orderPath $order
& $compose -GameDirectory $game -SettingsPath $settings
Assert (Test-Path -LiteralPath $stockFile) 'Ignored malformed mod broke composition'
if ($WithCustomInstallers) {
    $packages = @(Get-ChildItem -LiteralPath (Join-Path $repo 'dist') -Directory -Filter 'Ostrabus-Custom-*' | Sort-Object LastWriteTimeUtc -Descending)
    foreach ($withRotation in @($false,$true)) {
        $chosen = $null
        foreach ($candidate in $packages) {
            $plan = Get-Content -LiteralPath (Join-Path $candidate.FullName 'selection.json') -Raw | ConvertFrom-Json
            if (($withRotation -and $plan.included -contains 'rotation-module') -or (-not $withRotation -and @($plan.included).Count -eq 0)) { $chosen = $candidate; break }
        }
        if (-not $chosen) { throw 'Run CustomBuilder/Test-Installer.ps1 to produce current empty and rotation packages first.' }
        & (Join-Path $chosen.FullName 'Install-Custom.ps1') -GameDirectory $game -SettingsPath $settings
        Assert ((Get-FileHash -LiteralPath $plugin).Hash -eq (Get-FileHash -LiteralPath $dlls[0].FullName).Hash) 'Actual custom installer replaced standalone DLL'
        $stock = Get-Content -LiteralPath $stockFile -Raw | ConvertFrom-Json
        Assert ($stock.Count -eq 17 -and @($stock | Where-Object { $_.aLoots -notcontains 'ItmOstrabusApartmentRefuelStock=1.0x1' }).Count -eq 0) 'Actual custom installer lost terminal stock'
        Assert ((@($stock | Where-Object { $_.aLoots -match 'OstrabusNavRotation' }).Count -gt 0) -eq $withRotation) 'Actual custom installer did not refresh selected rotation stock'
    }
    # The custom installer intentionally replaces the omnibus; establish the new
    # control hash before checking our independent uninstaller leaves it alone.
    $omniHash = (Get-FileHash -LiteralPath $omni).Hash
}
& $install -GameDirectory $game -SettingsPath $settings -Uninstall
Assert (-not (Test-Path -LiteralPath $plugin) -and -not (Test-Path -LiteralPath $stockFile)) 'Uninstall leaves plugin/stock active'
Assert (Test-Path -LiteralPath (Join-Path $mods 'OstrabusApartmentRefuel\data\condowners\apartment_refuel.json')) 'Uninstall removed saved item definitions'
$order = Get-Content -LiteralPath $orderPath -Raw | ConvertFrom-Json
Assert ($order[0].aLoadOrder -contains 'OstrabusApartmentRefuel') 'Uninstall disables saved item definitions'
Assert ((Get-FileHash -LiteralPath $omni).Hash -eq $omniHash) 'Standalone installer changed main plugin'
& $install -GameDirectory $game -SettingsPath $settings
Assert ((Test-Path -LiteralPath $plugin) -and (Test-Path -LiteralPath $stockFile)) 'Reinstall after removal fails'
Write-Host "$checks apartment refuel installer checks passed. Fixture: $fixture"
