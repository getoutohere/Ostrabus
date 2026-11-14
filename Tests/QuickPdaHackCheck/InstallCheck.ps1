$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$installer=Join-Path $repo 'Build\Install-NativeMod.ps1'
$source=Join-Path $repo 'QoL\PdaHacking\Assets'
$fixture=Join-Path $repo ('obj\NativePdaInstall-'+[Guid]::NewGuid().ToString('N'))
$game=Join-Path $fixture 'Game'
$settings=Join-Path $fixture 'settings.json'
$order=Join-Path $game 'Ostranauts_Data\Mods\loading_order.json'
$script:checks=0
function Check([bool]$Ok,[string]$Why) { $script:checks++; if (-not $Ok) { throw $Why } }
function Write-Json([string]$Path,[object]$Value) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllText($Path,(ConvertTo-Json -InputObject $Value -Depth 64),[Text.UTF8Encoding]::new($false))
}
function Install { & $installer -ModName OstrabusPdaHack -AssetSource $source -GameDirectory $game -SettingsPath $settings }
Install
$records=Get-Content $order -Raw | ConvertFrom-Json
Check (($records[0].aLoadOrder -join ',') -eq 'core,OstrabusPdaHack') 'Fresh native order invalid.'
Check ((Get-Content $order -Raw).TrimStart().StartsWith('[')) 'Native order lost its array root.'
foreach ($asset in Get-ChildItem $source -Recurse -File) {
    $relative=$asset.FullName.Substring($source.Length).TrimStart('\','/')
    Check ((Get-FileHash $asset.FullName).Hash -eq (Get-FileHash (Join-Path (Split-Path $order -Parent) ('OstrabusPdaHack\'+$relative))).Hash) 'Installed asset differs.'
}
$before=(Get-FileHash $order).Hash
Install
Check ((Get-FileHash $order).Hash -eq $before) 'Repeated install changed native order.'
$order=Join-Path $fixture 'Custom Mods\loading_order.json'
Write-Json $settings @([ordered]@{strName='UserSettings';strPathMods=$order;keep=7})
Write-Json $order @([ordered]@{strName='Mod Loading Order';aLoadOrder=@('core','Other|disabled','TemplateSettings|disabled');aIgnorePatterns=@('keep');extra=42},[ordered]@{strName='Other Record';keep=9})
$before=[IO.File]::ReadAllText($order)
Install
$records=Get-Content $order -Raw | ConvertFrom-Json
Check (($records[0].aLoadOrder -join ',') -eq 'core,Other|disabled,TemplateSettings|disabled,OstrabusPdaHack') 'Other mods were reordered or toggled.'
Check ($records[0].extra -eq 42 -and $records[1].keep -eq 9 -and $records[0].aIgnorePatterns[0] -eq 'keep') 'Native metadata was lost.'
Check ([IO.File]::ReadAllText($order+'.OstrabusPdaHack.bak') -eq $before) 'Load-order backup differs.'
Check (Test-Path (Join-Path (Split-Path $order -Parent) 'OstrabusPdaHack\data\interaction_overrides\quick_pda.json')) 'Custom mod location ignored.'
& $installer -ModName OstrabusPdaHack -GameDirectory $game -SettingsPath $settings -Disabled
$records=Get-Content $order -Raw | ConvertFrom-Json
Check ($records[0].aLoadOrder -contains 'OstrabusPdaHack|disabled') 'Omitted native feature stayed enabled.'
$before=(Get-FileHash $order).Hash
Install
Check ((Get-FileHash $order).Hash -eq $before) 'Normal full deployment changed a deliberate disabled setting.'
& $installer -ModName OstrabusPdaHack -AssetSource $source -GameDirectory $game -SettingsPath $settings -Enable
$records=Get-Content $order -Raw | ConvertFrom-Json
Check ($records[0].aLoadOrder -contains 'OstrabusPdaHack' -and $records[0].aLoadOrder -notcontains 'OstrabusPdaHack|disabled') 'Explicit custom selection failed to reenable native feature.'
Check ($records[0].aLoadOrder -contains 'TemplateSettings|disabled') 'PDA hacking enabled the unrelated Settings app.'
foreach ($entries in @(@('Other'),@('core','OstrabusPdaHack','OstrabusPdaHack|disabled'))) {
    Write-Json $order @([ordered]@{strName='Mod Loading Order';aLoadOrder=$entries})
    $before=(Get-FileHash $order).Hash
    $rejected=$false
    try { Install } catch { $rejected=$true }
    Check $rejected 'Invalid core/duplicate native entries were accepted.'
    Check ((Get-FileHash $order).Hash -eq $before) 'Rejected install changed native order.'
}
Write-Json $order @([ordered]@{strName='Mod Loading Order';aLoadOrder=@('core','Other')})
$before=(Get-FileHash $order).Hash
& $installer -ModName OstrabusPdaHack -GameDirectory $game -SettingsPath $settings -Disabled
Check ((Get-FileHash $order).Hash -eq $before) 'Omitted never-installed feature created a load-order entry.'
Write-Output "Native PDA installer: $script:checks checks passed. Disposable fixture: $fixture"
