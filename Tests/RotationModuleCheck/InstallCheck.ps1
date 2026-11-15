$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$fixture=Join-Path $repo ('obj\RotationInstall-'+[Guid]::NewGuid().ToString('N'))
$game=Join-Path $fixture 'Game'
$settings=Join-Path $fixture 'absent-settings.json'
$installer=Join-Path $repo 'Build\Install-NativeMod.ps1'
$assets=Join-Path $repo 'NavConsole\Rotation\Assets'
$mods=Join-Path $game 'Ostranauts_Data\Mods'
$order=Join-Path $mods 'loading_order.json'
$stock=Join-Path $mods 'OstrabusRotation\data\loot\rotation_stock.json'
$definition=Join-Path $mods 'OstrabusRotation\data\cooverlays\rotation.json'
$checks=0
function Check([bool]$value,[string]$why) { $script:checks++; if (-not $value) { throw $why } }
function Omit { & $installer -ModName OstrabusRotation -GameDirectory $game -SettingsPath $settings -Disabled -RetainDefinitions -OmissionFiles 'data/loot/rotation_stock.json' }
Omit
Check (-not (Test-Path -LiteralPath $order)) 'Fresh omission created native assets/load order'
& $installer -ModName OstrabusRotation -AssetSource $assets -GameDirectory $game -SettingsPath $settings
$hash=(Get-FileHash -LiteralPath $definition).Hash
$orderHash=(Get-FileHash -LiteralPath $order).Hash
Check (Test-Path -LiteralPath $stock) 'Full install missed shop inventory'
Omit
Check ((Get-FileHash -LiteralPath $definition).Hash -eq $hash) 'Omission changed item definitions'
Check ((Get-FileHash -LiteralPath $order).Hash -eq $orderHash) 'Omission disabled existing definitions'
Check (-not (Test-Path -LiteralPath $stock)) 'Omission still injects future stock'
$backups=@(Get-ChildItem -LiteralPath (Split-Path -Parent $stock) -Filter '*.omitted-*')
Check ($backups.Count -eq 1) 'Original stock was not retained as an ignored backup'
Omit
Check (@(Get-ChildItem -LiteralPath (Split-Path -Parent $stock) -Filter '*.omitted-*').Count -eq 1) 'Repeated omission generated extra backups'
& $installer -ModName OstrabusRotation -AssetSource $assets -GameDirectory $game -SettingsPath $settings -Enable
Check (Test-Path -LiteralPath $stock) 'Reselection did not restore stock'
Check ((Get-FileHash -LiteralPath $definition).Hash -eq $hash) 'Reselection changed definitions'
$records=Get-Content -LiteralPath $order -Raw | ConvertFrom-Json
$records[0].aLoadOrder=@('core','OstrabusRotation|disabled')
[IO.File]::WriteAllText($order,(ConvertTo-Json -InputObject @($records) -Depth 8))
$orderHash=(Get-FileHash -LiteralPath $order).Hash
Omit
Check ((Get-FileHash -LiteralPath $order).Hash -eq $orderHash) 'Omission changed a manually disabled mod'
$rejected=$false
try { & $installer -ModName OstrabusRotation -GameDirectory $game -SettingsPath $settings -Disabled -RetainDefinitions -OmissionFiles 'data/../../../escaped.json' } catch { $rejected=$true }
Check $rejected 'Path traversal accepted'
Check ((Get-FileHash -LiteralPath $order).Hash -eq $orderHash) 'Rejected path changed load order'
Write-Host "Rotation native installer: $checks checks passed. Fixture: $fixture"
