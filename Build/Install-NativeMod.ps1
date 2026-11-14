[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidatePattern('^[A-Za-z][A-Za-z0-9_-]*$')][string]$ModName,
    [string]$AssetSource,
    [Parameter(Mandatory=$true)][string]$GameDirectory,
    [string]$SettingsPath=(Join-Path $env:USERPROFILE 'AppData\LocalLow\Blue Bottle Games\Ostranauts\settings.json'),
    [switch]$Enable,
    [switch]$Disabled
)
$ErrorActionPreference='Stop'
if ($Enable -and $Disabled) { throw 'Enable and Disabled cannot be combined.' }
$assets=@()
if (-not $Disabled) {
    if (-not $AssetSource) { throw 'An asset source is required for installation.' }
    $source=(Resolve-Path -LiteralPath $AssetSource).ProviderPath.TrimEnd('\','/')
    if (-not (Test-Path -LiteralPath (Join-Path $source 'mod_info.json') -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $source 'data') -PathType Container)) { throw "Incomplete native mod: $source" }
    $assets=@(Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object Name -ne '.gitkeep')
    foreach ($asset in $assets) {
        if ($asset.Extension -eq '.json') { $null=Get-Content -LiteralPath $asset.FullName -Raw | ConvertFrom-Json }
    }
}
$orderPath=Join-Path $GameDirectory 'Ostranauts_Data\Mods\loading_order.json'
if (Test-Path -LiteralPath $SettingsPath -PathType Leaf) {
    $settings=Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
    $user=$settings | Where-Object strName -eq 'UserSettings' | Select-Object -First 1
    if (-not [string]::IsNullOrWhiteSpace($user.strPathMods)) {
        $orderPath=Join-Path ([IO.Path]::GetDirectoryName($user.strPathMods)) 'loading_order.json'
    }
}
$orderPath=[IO.Path]::GetFullPath($orderPath)
$root=Split-Path -Parent $orderPath
$destination=[IO.Path]::GetFullPath((Join-Path $root $ModName))
if (-not $destination.StartsWith($root.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Native mod path escaped its root.' }
$exists=Test-Path -LiteralPath $orderPath -PathType Leaf
if ($exists) {
    $records=Get-Content -LiteralPath $orderPath -Raw | ConvertFrom-Json
    $records=@($records)
    $orders=@($records | Where-Object strName -eq 'Mod Loading Order')
    if ($orders.Count -ne 1 -or $null -eq $orders[0].aLoadOrder) { throw "Unrecognized native mod load order: $orderPath" }
    $order=$orders[0]
} else {
    if ($Disabled) { return }
    $order=[pscustomobject]@{strName='Mod Loading Order';aLoadOrder=@('core');aIgnorePatterns=@()}
    $records=@($order)
}
$entries=@($order.aLoadOrder)
$own=@($entries | Where-Object { ($_ -split '\|',2)[0] -eq $ModName })
if ($own.Count -gt 1) { throw "Duplicate native mod entries: $ModName" }
if (-not $Disabled -and $entries -notcontains 'core') { throw 'Cannot install native assets without an enabled core entry.' }
$changed=$false
if ($own.Count -eq 0) {
    if ($Disabled) { return }
    $entries+=@($ModName)
    $changed=$true
} else {
    $entries=@(foreach ($entry in $entries) {
        if (($entry -split '\|',2)[0] -ne $ModName) { $entry; continue }
        $next=$entry
        if ($Disabled -and $entry -notmatch '\|disabled(?:\||$)') { $next=$entry+'|disabled' }
        elseif ($Enable) { $next=$entry -replace '\|disabled(?=\||$)','' }
        if ($next -ne $entry) { $changed=$true }
        $next
    })
}
if (-not $Disabled) {
    foreach ($asset in $assets) {
        $relative=$asset.FullName.Substring($source.Length).TrimStart('\','/')
        $target=Join-Path $destination $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $asset.FullName -Destination $target -Force
        if ((Get-FileHash -LiteralPath $asset.FullName).Hash -ne (Get-FileHash -LiteralPath $target).Hash) {
            throw "Native asset copy verification failed: $relative"
        }
    }
}
if ($changed) {
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    if ($exists) { Copy-Item -LiteralPath $orderPath -Destination ($orderPath+'.'+$ModName+'.bak') -Force }
    $order.aLoadOrder=$entries
    $temporary=$orderPath+'.'+$ModName+'.tmp'
    [IO.File]::WriteAllText($temporary,(ConvertTo-Json -InputObject @($records | ForEach-Object { $_ }) -Depth 64),[Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $orderPath -Force
}
if ($Disabled) { Write-Host "Disabled native mod $ModName for this custom build." }
else {
    Write-Host "Installed native mod $ModName to $destination"
    if (-not $Enable -and $own.Count -gt 0 -and $own[0] -match '\|disabled(?:\||$)') {
        Write-Host "$ModName remains disabled, preserving your native Mods setting."
    }
}
