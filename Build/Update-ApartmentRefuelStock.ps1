[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GameDirectory,
    [string]$SettingsPath = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Blue Bottle Games\Ostranauts\settings.json')
)

# Installation-time composition, never a runtime/restock hook. Native loot records
# replace whole records; compose from enabled sources to preserve other shop mods.
$ErrorActionPreference = 'Stop'
$modName = 'OstrabusApartmentRefuel'
$orderPath = Join-Path $GameDirectory 'Ostranauts_Data\Mods\loading_order.json'
if (Test-Path -LiteralPath $SettingsPath -PathType Leaf) {
    $settings = Get-Content -LiteralPath $SettingsPath -Raw | ConvertFrom-Json
    $user = $settings | Where-Object strName -eq 'UserSettings' | Select-Object -First 1
    if (-not [string]::IsNullOrWhiteSpace($user.strPathMods)) {
        $orderPath = Join-Path ([IO.Path]::GetDirectoryName($user.strPathMods)) 'loading_order.json'
    }
}
if (-not (Test-Path -LiteralPath $orderPath -PathType Leaf)) { return }
$records = Get-Content -LiteralPath $orderPath -Raw | ConvertFrom-Json
$records = @($records)
$orders = @($records | Where-Object strName -eq 'Mod Loading Order')
if ($orders.Count -ne 1 -or $null -eq $orders[0].aLoadOrder) { throw 'Unrecognized native mod loading order.' }
$order = $orders[0]
$own = @($order.aLoadOrder | Where-Object { ($_ -split '\|',2)[0] -eq $modName })
if ($own.Count -eq 0) { return }
if ($own.Count -ne 1) { throw 'Duplicate apartment refuel native mod entries.' }
$modRoot = Split-Path -Parent $orderPath
$stockFile = Join-Path $modRoot "$modName\data\loot\supply_stock.json"
$plugin = Join-Path $GameDirectory 'BepInEx\plugins\OstrabusApartmentRefuel\Ostrabus.ApartmentRefuel.dll'
if ($own[0] -match '\|disabled(?:\||$)' -or -not (Test-Path -LiteralPath $plugin -PathType Leaf)) {
    if (Test-Path -LiteralPath $stockFile -PathType Leaf) {
        Move-Item -LiteralPath $stockFile -Destination ($stockFile + '.disabled-' + [Guid]::NewGuid().ToString('N'))
    }
    return
}

$stockIds = @('ItmSupplyKioskInv') + @('BCER','BCRS','EJDR','HQCH','JATL','JFTS','JPTN','MHNG','MLAB','MTRS','MSUZ','MVOL','SVIR','VCBR','VENC','VNCA' | ForEach-Object { "ItmSupplyKiosk$($_)Inv" })
$effective = @{}
# PowerShell 5 rejects JSON comments. Match complete quoted strings first, so
# comment-like text inside strings (including URLs/escaped quotes) is preserved.
function Read-LootJson([string]$Text) {
    $clean = [regex]::Replace($Text, '"(?:\\.|[^"\\])*"|//[^\r\n]*|/\*[\s\S]*?\*/', [Text.RegularExpressions.MatchEvaluator]{
        param($match)
        if ($match.Value.StartsWith('"')) { return $match.Value }
        return [regex]::Replace($match.Value, '[^\r\n]', ' ')
    })
    $rows = $clean | ConvertFrom-Json
    foreach ($row in $rows) { $row }
}
foreach ($entry in $order.aLoadOrder) {
    $parts = $entry -split '\|'
    if ($parts.Count -gt 2 -or ($parts.Count -eq 2 -and $parts[1] -eq 'disabled')) { continue }
    $path = $parts[0]
    $dataRoot = if ($path -eq 'core') { Join-Path $GameDirectory 'Ostranauts_Data\StreamingAssets\data' }
                elseif ([IO.Path]::IsPathRooted($path)) { Join-Path $path 'data' }
                else { Join-Path $modRoot "$path\data" }
    $lootRoot = Join-Path $dataRoot 'loot'
    if (-not (Test-Path -LiteralPath $lootRoot -PathType Container)) { continue }
    foreach ($file in [IO.Directory]::GetFiles($lootRoot, '*.json', [IO.SearchOption]::AllDirectories)) {
        if ([IO.Path]::GetFullPath($file) -eq [IO.Path]::GetFullPath($stockFile)) { continue }
        $sanitized = $file.Replace('\','/').Replace('//','/')
        $ignored = $false
        foreach ($pattern in $order.aIgnorePatterns) {
            if ($null -ne $pattern -and $sanitized.Contains($pattern.Replace('\','/').Replace('//','/'))) { $ignored = $true; break }
        }
        if ($ignored) { continue }
        $raw = [IO.File]::ReadAllText($file)
        if (-not $raw.Contains('ItmSupplyKiosk')) { continue }
        try { $rows = @(Read-LootJson $raw) }
        catch { throw "Cannot safely compose supply stock from $file : $($_.Exception.Message)" }
        foreach ($row in $rows) { if ($stockIds -ccontains $row.strName) { $effective[$row.strName] = $row } }
    }
}
$result = @(foreach ($id in $stockIds) {
    if (-not $effective.ContainsKey($id)) { throw "Missing native supply stock definition: $id" }
    $row = $effective[$id]
    $loots = @($row.aLoots | Where-Object { $_ -and $_ -notmatch '^ItmOstrabusApartmentRefuelStock=' })
    $loots += 'ItmOstrabusApartmentRefuelStock=1.0x1'
    $row | Add-Member -NotePropertyName aLoots -NotePropertyValue $loots -Force
    $row
})
# Validate everything before mutation. Keep our composed records after all stock
# contributors. Disabled states/order among unrelated mods remain untouched.
$newEntries = @($order.aLoadOrder | Where-Object { ($_ -split '\|',2)[0] -ne $modName }) + $own
$order.aLoadOrder = $newEntries
$stockJson = ConvertTo-Json -InputObject $result -Depth 64
$orderJson = ConvertTo-Json -InputObject $records -Depth 64
$utf8 = [Text.UTF8Encoding]::new($false)
New-Item -ItemType Directory -Path (Split-Path -Parent $stockFile) -Force | Out-Null
foreach ($output in @(@($stockFile,$stockJson), @($orderPath,$orderJson))) {
    if ((Test-Path -LiteralPath $output[0]) -and [IO.File]::ReadAllText($output[0]) -eq $output[1]) { continue }
    if (Test-Path -LiteralPath $output[0]) { Copy-Item -LiteralPath $output[0] -Destination ($output[0]+'.backup-'+[Guid]::NewGuid().ToString('N')) }
    [IO.File]::WriteAllText(($output[0]+'.tmp'), $output[1], $utf8)
    Move-Item -LiteralPath ($output[0]+'.tmp') -Destination $output[0] -Force
}
Write-Host 'Apartment refuel stock composed with enabled supply mods (17 shops).'
