[CmdletBinding()]
param(
    [string]$GameDirectory = $env:OSTRANAUTS_DIR,
    [string]$SettingsPath = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Blue Bottle Games\Ostranauts\settings.json'),
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'
$support = Join-Path $PSScriptRoot 'Support'
if (-not (Test-Path -LiteralPath $support)) { $support = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'Build' }
. (Join-Path $support 'Find-Game.ps1')
$gamePath = Find-Ostranauts -GameDirectory $GameDirectory
if (-not (Test-Path -LiteralPath (Join-Path $gamePath 'BepInEx\core\BepInEx.dll'))) { throw 'Install BepInEx 5 before this mod.' }
if (Get-Process -Name Ostranauts -ErrorAction SilentlyContinue) { throw 'Close Ostranauts before changing the mod.' }
$pluginRoot = Join-Path $gamePath 'BepInEx\plugins\OstrabusApartmentRefuel'
$dllPath = Join-Path $pluginRoot 'Ostrabus.ApartmentRefuel.dll'
$source = Join-Path $PSScriptRoot 'Plugin\Ostrabus.ApartmentRefuel.dll'
if (-not (Test-Path -LiteralPath $source)) { $source = Join-Path $PSScriptRoot 'bin\Release\netstandard2.1\Ostrabus.ApartmentRefuel.dll' }
if (-not $Uninstall -and -not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'Build the Release standalone project or use its packaged installer.' }
$orderPath = Join-Path $gamePath 'Ostranauts_Data\Mods\loading_order.json'
if (Test-Path -LiteralPath $SettingsPath -PathType Leaf) {
    $settings = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
    $user = $settings | Where-Object strName -eq UserSettings | Select-Object -First 1
    if (-not [string]::IsNullOrWhiteSpace($user.strPathMods)) { $orderPath = Join-Path ([IO.Path]::GetDirectoryName($user.strPathMods)) 'loading_order.json' }
}
$nativeRoot = Join-Path (Split-Path -Parent $orderPath) 'OstrabusApartmentRefuel'
$backupRoot = Join-Path $pluginRoot ('Backups\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
$snapshots = [Collections.Generic.List[object]]::new()
$targets = @($dllPath,$orderPath,(Join-Path $nativeRoot 'data\loot\supply_stock.json'))
if (-not $Uninstall) {
    $assetRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot 'Assets')).ProviderPath
    $targets += @(Get-ChildItem -LiteralPath $assetRoot -Recurse -File | ForEach-Object { Join-Path $nativeRoot $_.FullName.Substring($assetRoot.Length).TrimStart('\','/') })
}
foreach ($target in @($targets | Select-Object -Unique)) {
    $exists = Test-Path -LiteralPath $target -PathType Leaf
    $backup = Join-Path $backupRoot ($snapshots.Count.ToString() + '.bak')
    if ($exists) { Copy-Item -LiteralPath $target -Destination $backup }
    $snapshots.Add([pscustomobject]@{path=$target;existed=$exists;backup=$backup})
}
try {
    if ($Uninstall) {
        if (Test-Path -LiteralPath $dllPath) { Move-Item -LiteralPath $dllPath -Destination ($dllPath+'.disabled-'+[Guid]::NewGuid().ToString('N')) }
    } else {
        & (Join-Path $support 'Install-NativeMod.ps1') -ModName OstrabusApartmentRefuel -AssetSource $assetRoot -GameDirectory $gamePath -SettingsPath $SettingsPath -Enable
        New-Item -ItemType Directory -Path $pluginRoot -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $dllPath -Force
        if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $dllPath).Hash) { throw 'Standalone DLL copy verification failed.' }
    }
    & (Join-Path $support 'Update-ApartmentRefuelStock.ps1') -GameDirectory $gamePath -SettingsPath $SettingsPath
} catch {
    $failure = $_
    foreach ($snapshot in $snapshots) {
        if ($snapshot.existed) { Copy-Item -LiteralPath $snapshot.backup -Destination $snapshot.path -Force }
        elseif (Test-Path -LiteralPath $snapshot.path -PathType Leaf) { Remove-Item -LiteralPath $snapshot.path }
    }
    throw $failure
}
if ($Uninstall) { Write-Host 'Removed apartment refuel plugin and future stock. Item definitions and uninstall work remain for saved items.' }
else { Write-Host 'Installed Apartment Refuel Terminal. Restart Ostranauts; supply kiosks gain stock at their next native restock.' }
