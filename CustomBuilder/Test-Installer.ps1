[CmdletBinding()]
param([string]$GameDirectory=$env:OSTRANAUTS_DIR)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$script:checks=0
function Assert {
    param([bool]$Value,[string]$Message)
    $script:checks++
    if (-not $Value) { throw "Installer check failed: $Message" }
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path).Hash }
function Write-Json([string]$Path,[object]$Value) { [IO.File]::WriteAllText($Path,(ConvertTo-Json -InputObject $Value -Depth 30),[Text.UTF8Encoding]::new($false)) }
Push-Location $repo
try {
    $normal=Join-Path $repo 'bin\Debug\netstandard2.1\Ostrabus.dll'
    $normalHash=if (Test-Path -LiteralPath $normal) { Hash $normal } else { '' }
    & (Join-Path $PSScriptRoot 'Build-Custom.ps1') -Preset empty -NoMenu -GameDirectory $GameDirectory
    $empty=Get-ChildItem -LiteralPath (Join-Path $repo 'dist') -Directory -Filter 'Ostrabus-Custom-empty-*' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    & (Join-Path $PSScriptRoot 'Build-Custom.ps1') -ProfilePath (Join-Path $PSScriptRoot 'profile.example.json') -GameDirectory $GameDirectory
    $pda=Get-ChildItem -LiteralPath (Join-Path $repo 'dist') -Directory -Filter 'Ostrabus-Custom-crew-and-nav-*' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    Assert ($null -ne $empty -and $null -ne $pda) 'both packages produced'
    foreach ($package in @($empty,$pda)) {
        $dlls=@(Get-ChildItem -LiteralPath $package.FullName -Recurse -Filter '*.dll')
        Assert ($dlls.Count -eq 1 -and $dlls[0].Name -eq 'Ostrabus.dll') 'no game/tool DLL redistribution'
        foreach ($file in Get-ChildItem -LiteralPath $package.FullName -Recurse -File | Where-Object { $_.Extension -in @('.json','.ps1','.txt','.cmd') }) {
            Assert (-not ([IO.File]::ReadAllText($file.FullName) -match '[A-Za-z]:[\\/]Users[\\/]')) 'package text contains no author user-directory paths'
        }
    }
    Assert (-not (Test-Path -LiteralPath (Join-Path $empty.FullName 'PdaSettings'))) 'empty package omits native assets'
    Assert (Test-Path -LiteralPath (Join-Path $pda.FullName 'PdaSettings\Assets\mod_info.json')) 'PDA package includes native assets'
    $fixture=Join-Path $repo ('obj\CustomBuilder\installer-'+[Guid]::NewGuid().ToString('N'))
    $game=Join-Path $fixture 'Test Game'
    $mods=Join-Path $fixture 'Custom Mods'
    $plugins=Join-Path $game 'BepInEx\plugins'
    $plugin=Join-Path $plugins 'Ostrabus'
    foreach ($folder in @('Ostranauts_Data\Managed','BepInEx\core','BepInEx\plugins\Ostrabus','BepInEx\plugins\TemplateExample')) { New-Item -ItemType Directory -Path (Join-Path $game $folder) -Force | Out-Null }
    New-Item -ItemType Directory -Path $mods -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $game 'Ostranauts_Data\Managed\Assembly-CSharp.dll'),'fixture')
    [IO.File]::WriteAllText((Join-Path $game 'BepInEx\core\BepInEx.dll'),'fixture')
    [IO.File]::WriteAllText((Join-Path $plugin 'Ostrabus.dll'),'previous DLL')
    [IO.File]::WriteAllText((Join-Path $plugin 'Ostrabus.pdb'),'previous PDB')
    [IO.File]::WriteAllText((Join-Path $plugins 'TemplateExample\TemplateExample.dll'),'legacy')
    [IO.File]::WriteAllText((Join-Path $plugins 'OtherPlugin.dll'),'other plugin')
    $orderPath=Join-Path $mods 'loading_order.json'
    $settings=Join-Path $fixture 'settings.json'
    Write-Json $settings @([ordered]@{strName='UserSettings';strPathMods=$orderPath;unrelated=42})
    Write-Json $orderPath @([ordered]@{strName='Mod Loading Order';aLoadOrder=@('core','Other|disabled','TemplateSettings');aIgnorePatterns=@('keep-me');unknownMetadata='retained'},[ordered]@{strName='Other Record';value=99})
    $config=Join-Path $fixture 'unchanged.cfg'
    [IO.File]::WriteAllText($config,'leave my settings alone')
    & (Join-Path $empty.FullName 'Install-Custom.ps1') -GameDirectory $game -SettingsPath $settings
    Assert ((Hash (Join-Path $plugin 'Ostrabus.dll')) -eq (Hash (Join-Path $empty.FullName 'Plugin\Ostrabus.dll'))) 'custom DLL installed'
    Assert (-not (Test-Path -LiteralPath (Join-Path $plugins 'TemplateExample\TemplateExample.dll'))) 'predecessor disabled'
    $order=Get-Content -LiteralPath $orderPath -Raw | ConvertFrom-Json
    $order=@($order)
    Assert ($order[0].aLoadOrder -contains 'TemplateSettings|disabled') 'no-PDA profile disables only own entry'
    Assert ($order[0].aLoadOrder -contains 'Other|disabled') 'other disabled mod preserved'
    Assert ($order[0].unknownMetadata -eq 'retained' -and $order[1].value -eq 99 -and $order[0].aIgnorePatterns[0] -eq 'keep-me') 'load order metadata preserved'
    & (Join-Path $pda.FullName 'Install-Custom.ps1') -GameDirectory $game -SettingsPath $settings
    $order=Get-Content -LiteralPath $orderPath -Raw | ConvertFrom-Json
    $order=@($order)
    Assert ($order[0].aLoadOrder -contains 'TemplateSettings' -and $order[0].aLoadOrder -notcontains 'TemplateSettings|disabled') 'PDA profile reenables its entry'
    Assert (Test-Path -LiteralPath (Join-Path $mods 'TemplateSettings\data\pda_apps\template_settings.json')) 'custom mod location honored'
    Assert (@(Get-ChildItem -LiteralPath $plugins -Recurse -Filter '*.dll').Count -eq 2) 'only installed plugin and unrelated DLL discoverable'
    Assert ([IO.File]::ReadAllText((Join-Path $plugins 'OtherPlugin.dll')) -eq 'other plugin') 'other plugin unchanged'
    Assert ([IO.File]::ReadAllText($config) -eq 'leave my settings alone') 'configuration unchanged'
    # Permit backup reads but deny replacement of the PDB: fail after assets/load
    # order and the DLL changed, then require complete file rollback.
    $beforeDll=Hash (Join-Path $plugin 'Ostrabus.dll'); $beforeOrder=Hash $orderPath
    $locked=[IO.File]::Open((Join-Path $plugin 'Ostrabus.pdb'),[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    $rejected=$false
    try { & (Join-Path $empty.FullName 'Install-Custom.ps1') -GameDirectory $game -SettingsPath $settings } catch { $rejected=$true } finally { $locked.Dispose() }
    Assert $rejected 'blocked write rejected'
    Assert ((Hash (Join-Path $plugin 'Ostrabus.dll')) -eq $beforeDll) 'DLL rolled back after later failure'
    Assert ((Hash $orderPath) -eq $beforeOrder) 'load order rolled back after later failure'
    $sourceManifest=Join-Path $empty.FullName 'package.json'
    $manifestText=[IO.File]::ReadAllText($sourceManifest)
    try {
        $bad=$manifestText | ConvertFrom-Json
        $bad.pluginFiles.'Ostrabus.dll'='bad-hash'
        Write-Json $sourceManifest $bad
        $rejected=$false
        try { & (Join-Path $empty.FullName 'Install-Custom.ps1') -GameDirectory $game -SettingsPath $settings } catch { $rejected=$true }
        Assert $rejected 'checksum failure rejected before installation'
        Assert ((Hash (Join-Path $plugin 'Ostrabus.dll')) -eq $beforeDll -and (Hash $orderPath) -eq $beforeOrder) 'checksum failure leaves installation unchanged'
    } finally { [IO.File]::WriteAllText($sourceManifest,$manifestText,[Text.UTF8Encoding]::new($false)) }
    if ($normalHash) { Assert ((Hash $normal) -eq $normalHash) 'normal maintainer build unchanged' }
    Write-Host "Custom installer: $script:checks checks passed. Disposable fixture: $fixture" -ForegroundColor Green
} finally { Pop-Location }
