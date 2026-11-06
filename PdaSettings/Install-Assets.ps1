[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$AssetSource,
    [Parameter(Mandatory = $true)][string]$GameDirectory,
    [string]$SettingsPath = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Blue Bottle Games\Ostranauts\settings.json')
)

$ErrorActionPreference = 'Stop'
$nativeModName = 'TemplateSettings'
$assetSourcePath = (Resolve-Path -LiteralPath $AssetSource).ProviderPath
foreach ($assetRequired in @('mod_info.json', 'data\pda_apps\template_settings.json', 'data\strings\template_settings.json', 'images\pda_apps\IcoTemplateSettings.png')) {
    $assetRequiredPath = Join-Path $assetSourcePath $assetRequired
    if (-not (Test-Path -LiteralPath $assetRequiredPath -PathType Leaf)) { throw "Missing PDA asset: $assetRequiredPath" }
    if ($assetRequired.EndsWith('.json')) { $null = Get-Content -LiteralPath $assetRequiredPath -Raw | ConvertFrom-Json }
}

$loadingOrderPath = Join-Path $GameDirectory 'Ostranauts_Data\Mods\loading_order.json'
if (Test-Path -LiteralPath $SettingsPath -PathType Leaf) {
    $settingsRecords = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
    $userSettings = $settingsRecords | Where-Object { $_.strName -eq 'UserSettings' } | Select-Object -First 1
    if (-not [string]::IsNullOrWhiteSpace($userSettings.strPathMods)) {
        $loadingOrderPath = Join-Path ([IO.Path]::GetDirectoryName($userSettings.strPathMods)) 'loading_order.json'
    }
}
$loadingOrderPath = [IO.Path]::GetFullPath($loadingOrderPath)
# Vanilla settings store the loading_order.json filename, not just a Mods folder.
$nativeModRoot = Split-Path -Parent $loadingOrderPath
$nativeDestination = Join-Path $nativeModRoot $nativeModName
$orderExists = Test-Path -LiteralPath $loadingOrderPath -PathType Leaf
if ($orderExists) {
    $orderRecords = Get-Content -LiteralPath $loadingOrderPath -Raw | ConvertFrom-Json
    $orderRecords = @($orderRecords)
    $orderRecord = @($orderRecords | Where-Object { $_.strName -eq 'Mod Loading Order' })
    if ($orderRecord.Count -ne 1 -or $null -eq $orderRecord[0].aLoadOrder) { throw "Unrecognized native mod load order: $loadingOrderPath" }
    $orderRecord = $orderRecord[0]
} else {
    $orderRecord = [pscustomobject]@{ strName = 'Mod Loading Order'; aLoadOrder = @('core'); aIgnorePatterns = @() }
    $orderRecords = @($orderRecord)
}
$existingEntries = @($orderRecord.aLoadOrder)
$nativeEntries = @($existingEntries | Where-Object { ($_ -split '\|', 2)[0] -eq $nativeModName })
if ($nativeEntries.Count -eq 0) {
    if (-not ($existingEntries -contains 'core')) { throw 'Cannot register PDA assets: the native load order has no enabled core entry.' }
    $orderRecord.aLoadOrder = @($existingEntries + $nativeModName)
}

New-Item -ItemType Directory -Path $nativeDestination -Force | Out-Null
foreach ($assetFile in (Get-ChildItem -LiteralPath $assetSourcePath -Recurse -File | Where-Object Name -ne '.gitkeep')) {
    $assetRelative = $assetFile.FullName.Substring($assetSourcePath.Length).TrimStart('\', '/')
    $assetDestination = Join-Path $nativeDestination $assetRelative
    New-Item -ItemType Directory -Path (Split-Path -Parent $assetDestination) -Force | Out-Null
    Copy-Item -LiteralPath $assetFile.FullName -Destination $assetDestination -Force
    if ((Get-FileHash -LiteralPath $assetFile.FullName).Hash -ne (Get-FileHash -LiteralPath $assetDestination).Hash) { throw "PDA asset verification failed: $assetRelative" }
}
if ($nativeEntries.Count -eq 0) {
    # Keep a recoverable copy and serialize the original records, including unknown metadata.
    if ($orderExists) { Copy-Item -LiteralPath $loadingOrderPath -Destination ($loadingOrderPath + '.TemplateSettings.bak') -Force }
    $orderJson = ConvertTo-Json -InputObject @($orderRecords | ForEach-Object { $_ }) -Depth 64
    $orderTemporary = $loadingOrderPath + '.TemplateSettings.tmp'
    [IO.File]::WriteAllText($orderTemporary, $orderJson, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $orderTemporary -Destination $loadingOrderPath -Force
    Write-Host "Registered $nativeModName in $loadingOrderPath"
} elseif ($nativeEntries[0] -match '\|disabled') {
    Write-Host 'PDA assets updated; your existing disabled entry was preserved. Enable Ostrabus Settings in the native Mods screen to use the app.'
}
Write-Host "Installed PDA assets to $nativeDestination"
