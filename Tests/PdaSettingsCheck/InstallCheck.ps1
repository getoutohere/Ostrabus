$ErrorActionPreference = 'Stop'
$testRoot = Join-Path $PSScriptRoot ('..\..\obj\PdaInstall-' + [guid]::NewGuid().ToString('N'))
$testRoot = [IO.Path]::GetFullPath($testRoot)
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$testSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\PdaSettings\Assets'))
$testInstaller = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\PdaSettings\Install-Assets.ps1'))
$script:checks = 0
function Check($ok, $reason) { $script:checks++; if (-not $ok) { throw $reason } }
function WriteJson($path, $value) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    ConvertTo-Json -InputObject @($value | ForEach-Object { $_ }) -Depth 20 | Set-Content -LiteralPath $path -Encoding UTF8
}

$testGame = Join-Path $testRoot 'Game'
$testSettings = Join-Path $testRoot 'settings.json'
& $testInstaller -AssetSource $testSource -GameDirectory $testGame -SettingsPath $testSettings
$testOrder = Join-Path $testGame 'Ostranauts_Data\Mods\loading_order.json'
$testResult = Get-Content -LiteralPath $testOrder -Raw | ConvertFrom-Json
Check ($testResult.Count -eq 1 -and ($testResult[0].aLoadOrder -join ',') -eq 'core,TemplateSettings') 'Fresh install order incorrect'
Check ((Get-Content -LiteralPath $testOrder -Raw).TrimStart().StartsWith('[')) 'Root JSON array lost'
foreach($file in @('mod_info.json','data\pda_apps\template_settings.json','data\strings\template_settings.json','images\pda_apps\IcoTemplateSettings.png')) {
    Check ((Get-FileHash -LiteralPath (Join-Path $testSource $file)).Hash -eq (Get-FileHash -LiteralPath (Join-Path (Split-Path -Parent $testOrder) ('TemplateSettings\'+$file))).Hash) 'Installed asset differs'
}
$testHash = (Get-FileHash -LiteralPath $testOrder).Hash
& $testInstaller -AssetSource $testSource -GameDirectory $testGame -SettingsPath $testSettings
Check ((Get-FileHash -LiteralPath $testOrder).Hash -eq $testHash) 'Repeated install rewrote order'

$testCustom = Join-Path $testRoot 'Custom Mods\loading_order.json'
WriteJson $testSettings @([pscustomobject]@{strName='UserSettings';strPathMods=$testCustom})
WriteJson $testCustom @([pscustomobject]@{strName='Other record';keep=42},[pscustomobject]@{strName='Mod Loading Order';aLoadOrder=@('core','OtherMod|disabled','LastMod');aIgnorePatterns=@('keep_me');extra='preserve'})
$testOriginal = Get-Content -LiteralPath $testCustom -Raw
& $testInstaller -AssetSource $testSource -GameDirectory $testGame -SettingsPath $testSettings
$testResult = Get-Content -LiteralPath $testCustom -Raw | ConvertFrom-Json
Check ($testResult[0].keep -eq 42 -and $testResult[1].extra -eq 'preserve') 'Unknown metadata lost'
Check (($testResult[1].aLoadOrder -join ',') -eq 'core,OtherMod|disabled,LastMod,TemplateSettings') 'Existing entries reordered or altered'
Check ($testResult[1].aIgnorePatterns[0] -eq 'keep_me') 'Ignore patterns lost'
Check ((Get-Content -LiteralPath ($testCustom+'.TemplateSettings.bak') -Raw) -eq $testOriginal) 'Backup differs'
Check (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $testCustom) 'TemplateSettings\mod_info.json')) 'Custom asset destination ignored'

$testResult[1].aLoadOrder=@('core','TemplateSettings|disabled','OtherMod')
WriteJson $testCustom $testResult
$testHash = (Get-FileHash -LiteralPath $testCustom).Hash
& $testInstaller -AssetSource $testSource -GameDirectory $testGame -SettingsPath $testSettings
Check ((Get-FileHash -LiteralPath $testCustom).Hash -eq $testHash) 'Disabled state changed'
WriteJson $testCustom @([pscustomobject]@{strName='Mod Loading Order';aLoadOrder=@('OtherMod')})
$testHash = (Get-FileHash -LiteralPath $testCustom).Hash
$testRejected=$false
try { & $testInstaller -AssetSource $testSource -GameDirectory $testGame -SettingsPath $testSettings } catch { $testRejected=$true }
Check $testRejected 'Missing core was accepted'
Check ((Get-FileHash -LiteralPath $testCustom).Hash -eq $testHash) 'Rejected installation changed load order'
WriteJson $testCustom @([pscustomobject]@{strName='Unknown'})
$testRejected=$false
try { & $testInstaller -AssetSource $testSource -GameDirectory $testGame -SettingsPath $testSettings } catch { $testRejected=$true }
Check $testRejected 'Malformed load-order schema was accepted'
Write-Output "$script:checks installer checks passed. Fixtures: $testRoot"
