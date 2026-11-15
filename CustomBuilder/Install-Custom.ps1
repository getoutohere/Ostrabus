[CmdletBinding()]
param(
    [string]$GameDirectory=$env:OSTRANAUTS_DIR,
    [string]$SettingsPath=(Join-Path $env:USERPROFILE 'AppData\LocalLow\Blue Bottle Games\Ostranauts\settings.json')
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Support\Find-Game.ps1')
$gamePath=Find-Ostranauts -GameDirectory $GameDirectory
if (-not (Test-Path -LiteralPath (Join-Path $gamePath 'BepInEx\core\BepInEx.dll') -PathType Leaf)) { throw 'Install BepInEx 5 before installing Ostrabus.' }
$selection=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'selection.json') -Raw | ConvertFrom-Json
$manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'package.json') -Raw | ConvertFrom-Json
if ($selection.schemaVersion -ne 1 -or $manifest.schemaVersion -ne 1 -or $manifest.kind -ne 'Ostrabus custom build' -or $manifest.selectionHash -ne $selection.fingerprint) { throw 'Invalid custom package metadata.' }
foreach ($name in @('Ostrabus.dll','Ostrabus.pdb')) {
    $source=Join-Path $PSScriptRoot ('Plugin\'+$name)
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $manifest.pluginFiles.$name) { throw "Package checksum mismatch: $name" }
}
$loadingOrderPath=Join-Path $gamePath 'Ostranauts_Data\Mods\loading_order.json'
if (Test-Path -LiteralPath $SettingsPath -PathType Leaf) {
    $settingsRecords=Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
    $userSettings=$settingsRecords | Where-Object { $_.strName -eq 'UserSettings' } | Select-Object -First 1
    if (-not [string]::IsNullOrWhiteSpace($userSettings.strPathMods)) { $loadingOrderPath=Join-Path ([IO.Path]::GetDirectoryName($userSettings.strPathMods)) 'loading_order.json' }
}
$loadingOrderPath=[IO.Path]::GetFullPath($loadingOrderPath)
$modRoot=Split-Path -Parent $loadingOrderPath
if (Test-Path -LiteralPath $loadingOrderPath -PathType Leaf) {
    $records=Get-Content -LiteralPath $loadingOrderPath -Raw | ConvertFrom-Json
    $records=@($records)
    $order=@($records | Where-Object { $_.strName -eq 'Mod Loading Order' })
    if ($order.Count -ne 1 -or $null -eq $order[0].aLoadOrder) { throw 'Unrecognized mod load order; nothing installed.' }
}
if ($selection.pdaAssets) {
    foreach ($name in @('mod_info.json','data\pda_apps\template_settings.json','data\strings\template_settings.json','images\pda_apps\IcoTemplateSettings.png')) {
        $path=Join-Path $PSScriptRoot ('PdaSettings\Assets\'+$name)
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing PDA asset: $name" }
        if ($name.EndsWith('.json')) { $null=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
    }
}
$nativeMods=@($selection.nativeMods | Where-Object { $_ })
$managedNativeMods=@($manifest.managedNativeMods | Where-Object { $_ })
$retainedNativeMods=@($manifest.retainedNativeMods | Where-Object { $_ })
foreach ($mod in $retainedNativeMods) {
    if ($managedNativeMods -notcontains $mod.name -or $mod.name -notmatch '^[A-Za-z][A-Za-z0-9_-]*$') { throw 'Unknown retained native mod.' }
    foreach ($relative in @($mod.omissionFiles)) {
        $nativeRoot=[IO.Path]::GetFullPath((Join-Path $modRoot $mod.name)).TrimEnd('\')
        $nativeFile=[IO.Path]::GetFullPath((Join-Path $nativeRoot $relative))
        if ($relative -notmatch '^data[/\\]' -or $relative -notmatch '\.json$' -or -not $nativeFile.StartsWith($nativeRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid retained native mod omission file.' }
    }
}
foreach ($name in $managedNativeMods) {
    if ($name -notmatch '^[A-Za-z][A-Za-z0-9_-]*$') { throw "Invalid managed native mod: $name" }
}
if (@($managedNativeMods | Sort-Object -Unique).Count -ne $managedNativeMods.Count -or
    @($nativeMods.name | Sort-Object -Unique).Count -ne $nativeMods.Count) { throw 'Duplicate native mod selection.' }
foreach ($mod in $nativeMods) {
    if ($managedNativeMods -notcontains $mod.name) { throw "Unknown selected native mod: $($mod.name)" }
    $source=Join-Path $PSScriptRoot ('NativeMods\'+$mod.name)
    if (-not (Test-Path -LiteralPath (Join-Path $source 'mod_info.json') -PathType Leaf)) { throw "Missing native mod: $($mod.name)" }
    $assets=@(Get-ChildItem -LiteralPath $source -Recurse -File)
    $expectedFiles=@($manifest.nativeModFiles.PSObject.Properties | Where-Object { $_.Name.StartsWith($mod.name+'/',[StringComparison]::Ordinal) })
    if ($assets.Count -ne $expectedFiles.Count) { throw "Native mod file inventory mismatch: $($mod.name)" }
    foreach ($asset in $assets) {
        $relative=$asset.FullName.Substring($source.Length).TrimStart('\','/').Replace('\','/')
        $key=$mod.name+'/'+$relative
        $expected=$manifest.nativeModFiles.PSObject.Properties[$key]
        if (-not $expected -or (Get-FileHash -LiteralPath $asset.FullName -Algorithm SHA256).Hash -ne $expected.Value) {
            throw "Native mod checksum mismatch: $key"
        }
        if ($asset.Extension -eq '.json') { $null=Get-Content -LiteralPath $asset.FullName -Raw | ConvertFrom-Json }
    }
}
foreach ($process in @(Get-Process -Name 'Ostranauts' -ErrorAction SilentlyContinue)) {
    if (-not $process.Path -or [IO.Path]::GetFullPath($process.Path) -eq (Join-Path $gamePath 'Ostranauts.exe')) { throw 'Close Ostranauts before replacing its plugin.' }
}
$principal=[Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
$programFiles=@([Environment]::GetFolderPath('ProgramFiles'),[Environment]::GetFolderPath('ProgramFilesX86'))
$needsAdmin=@($programFiles | Where-Object { $_ -and $gamePath.StartsWith($_.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) }).Count -gt 0
if ($needsAdmin -and -not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    # Keep the caller's settings path when UAC uses a different administrator account.
    $arguments='-NoProfile -ExecutionPolicy Bypass -File "{0}" -GameDirectory "{1}" -SettingsPath "{2}"' -f $PSCommandPath,$gamePath,$SettingsPath
    $child=Start-Process -FilePath "$PSHOME\powershell.exe" -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -Wait -PassThru
    if ($child.ExitCode -ne 0) { throw 'Elevated custom installation failed; the installer retained rollback backups.' }
    return
}
$plugins=Join-Path $gamePath 'BepInEx\plugins'
$destination=Join-Path $plugins 'Ostrabus'
$backupRoot=Join-Path $destination ('install-backup-'+[Guid]::NewGuid().ToString('N'))
$backups=[Collections.Generic.List[object]]::new()
function Backup-File {
    param([string]$Path)
    $absolute=[IO.Path]::GetFullPath($Path)
    if (@($backups | Where-Object { $_.path -eq $absolute }).Count) { return }
    $existed=Test-Path -LiteralPath $absolute -PathType Leaf
    $backup=Join-Path $backupRoot ($backups.Count.ToString()+'.bak')
    if ($existed) { Copy-Item -LiteralPath $absolute -Destination $backup }
    $backups.Add([pscustomobject]@{path=$absolute; existed=$existed; backup=$backup})
}
function Write-LoadOrder {
    $records=Get-Content -LiteralPath $loadingOrderPath -Raw | ConvertFrom-Json
    $records=@($records)
    $record=@($records | Where-Object { $_.strName -eq 'Mod Loading Order' })[0]
    $changed=$false
    $entries=@(foreach ($entry in @($record.aLoadOrder)) {
        if (($entry -split '\|',2)[0] -eq 'TemplateSettings') {
            $updated=if ($selection.pdaAssets) { $entry -replace '\|disabled','' } elseif ($entry -match '\|disabled') { $entry } else { $entry+'|disabled' }
            if ($updated -ne $entry) { $changed=$true }
            $updated
        } else { $entry }
    })
    if ($changed) {
        $record.aLoadOrder=$entries
        $temporary=$loadingOrderPath+'.OstrabusCustom.tmp'
        [IO.File]::WriteAllText($temporary,(ConvertTo-Json -InputObject $records -Depth 64),[Text.UTF8Encoding]::new($false))
        Move-Item -LiteralPath $temporary -Destination $loadingOrderPath -Force
    }
}
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
try {
    # Backups deliberately end in .bak; BepInEx must not discover a second DLL.
    foreach ($name in @('Ostrabus.dll','Ostrabus.pdb')) { Backup-File (Join-Path $destination $name) }
    Backup-File $loadingOrderPath
    if ($selection.pdaAssets) {
        foreach ($asset in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'PdaSettings\Assets') -Recurse -File) {
            $relative=$asset.FullName.Substring((Join-Path $PSScriptRoot 'PdaSettings\Assets').Length).TrimStart('\','/')
            Backup-File (Join-Path $modRoot ('TemplateSettings\'+$relative))
        }
        & (Join-Path $PSScriptRoot 'Support\Install-Assets.ps1') -AssetSource (Join-Path $PSScriptRoot 'PdaSettings\Assets') -GameDirectory $gamePath -SettingsPath $SettingsPath
    }
    foreach ($name in $managedNativeMods) {
        if ($nativeMods.name -contains $name) {
            $source=Join-Path $PSScriptRoot ('NativeMods\'+$name)
            foreach ($asset in Get-ChildItem -LiteralPath $source -Recurse -File) {
                $relative=$asset.FullName.Substring($source.Length).TrimStart('\','/')
                Backup-File (Join-Path $modRoot ($name+'\'+$relative))
            }
            & (Join-Path $PSScriptRoot 'Support\Install-NativeMod.ps1') -ModName $name -AssetSource $source -GameDirectory $gamePath -SettingsPath $SettingsPath -Enable
        } else {
            $retain=$retainedNativeMods | Where-Object name -eq $name | Select-Object -First 1
            if ($retain) {
                foreach ($relative in @($retain.omissionFiles)) { Backup-File (Join-Path $modRoot ($name+'\'+$relative)) }
                & (Join-Path $PSScriptRoot 'Support\Install-NativeMod.ps1') -ModName $name -GameDirectory $gamePath -SettingsPath $SettingsPath -Disabled -RetainDefinitions -OmissionFiles @($retain.omissionFiles)
            } else {
                & (Join-Path $PSScriptRoot 'Support\Install-NativeMod.ps1') -ModName $name -GameDirectory $gamePath -SettingsPath $SettingsPath -Disabled
            }
        }
    }
    if (Test-Path -LiteralPath $loadingOrderPath -PathType Leaf) { Write-LoadOrder }
    Backup-File (Join-Path $modRoot 'OstrabusApartmentRefuel\data\loot\supply_stock.json')
    & (Join-Path $PSScriptRoot 'Support\Update-ApartmentRefuelStock.ps1') -GameDirectory $gamePath -SettingsPath $SettingsPath
    foreach ($name in @('Ostrabus.dll','Ostrabus.pdb')) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('Plugin\'+$name)) -Destination (Join-Path $destination $name) -Force
        if ((Get-FileHash -LiteralPath (Join-Path $destination $name)).Hash -ne $manifest.pluginFiles.$name) { throw "Installed checksum mismatch: $name" }
    }
    foreach ($relative in @('TemplateExample','Ostrabus','')) {
        $folder=if ($relative) { Join-Path $plugins $relative } else { $plugins }
        foreach ($name in @('TemplateExample.dll','TemplateExample.pdb')) {
            $legacy=Join-Path $folder $name
            if (Test-Path -LiteralPath $legacy -PathType Leaf) { Backup-File $legacy; Remove-Item -LiteralPath $legacy }
        }
    }
    [IO.File]::WriteAllText((Join-Path $backupRoot 'rollback-files.json'),(ConvertTo-Json -InputObject @($backups.ToArray()) -Depth 8),[Text.UTF8Encoding]::new($false))
    Write-Host "Installed custom profile '$($selection.name)' ($($selection.fingerprint.Substring(0,12))) to $destination" -ForegroundColor Green
    Write-Host "Reversible file backups: $backupRoot"
} catch {
    $failure=$_
    $rollbackErrors=[Collections.Generic.List[string]]::new()
    for ($index=$backups.Count-1; $index -ge 0; $index--) {
        $item=$backups[$index]
        try {
            if ($item.existed) {
                if (-not (Test-Path -LiteralPath $item.path -PathType Leaf) -or (Get-FileHash -LiteralPath $item.backup).Hash -ne (Get-FileHash -LiteralPath $item.path).Hash) { Copy-Item -LiteralPath $item.backup -Destination $item.path -Force }
            } elseif (Test-Path -LiteralPath $item.path -PathType Leaf) { Remove-Item -LiteralPath $item.path }
        } catch { $rollbackErrors.Add($_.Exception.Message) }
    }
    if ($rollbackErrors.Count) { Write-Warning "Rollback needs attention; backups retained at ${backupRoot}: $($rollbackErrors -join '; ')" }
    throw $failure
}
