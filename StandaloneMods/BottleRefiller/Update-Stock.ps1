[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GameDirectory,
    [Parameter(Mandatory=$true)][string]$OrderPath,
    [switch]$RemoveStock
)
$ErrorActionPreference='Stop'
$modName='OstrabusBottleRefiller'
$records=Get-Content -LiteralPath $OrderPath -Raw | ConvertFrom-Json
$records=@($records)
$orders=@($records | Where-Object strName -eq 'Mod Loading Order')
if($orders.Count -ne 1 -or $null -eq $orders[0].aLoadOrder){throw 'Unrecognized native mod loading order.'}
$order=$orders[0];$modRoot=Split-Path -Parent $OrderPath
$own=@($order.aLoadOrder | Where-Object {($_ -split '\|',2)[0] -eq $modName})
if($own.Count -ne 1){throw 'Expected one Bottle Refiller mod entry.'}
$stockFile=Join-Path $modRoot "$modName/data/loot/supply_stock.json"
function Read-Loot([string]$Text){
    $clean=[regex]::Replace($Text,'"(?:\\.|[^"\\])*"|//[^\r\n]*|/\*[\s\S]*?\*/',[Text.RegularExpressions.MatchEvaluator]{
        param($match)
        if($match.Value.StartsWith('"')){return $match.Value}
        return [regex]::Replace($match.Value,'[^\r\n]',' ')
    })
    $rows=$clean | ConvertFrom-Json;foreach($row in $rows){$row}
}
$effective=@{}
foreach($entry in $order.aLoadOrder){
    $parts=$entry -split '\|'
    if($parts.Count -gt 2 -or ($parts.Count -eq 2 -and $parts[1] -eq 'disabled')){continue}
    $path=$parts[0]
    $root=if($path -eq 'core'){Join-Path $GameDirectory 'Ostranauts_Data/StreamingAssets/data'}
          elseif([IO.Path]::IsPathRooted($path)){Join-Path $path 'data'}else{Join-Path $modRoot "$path/data"}
    $lootRoot=Join-Path $root 'loot'
    if(-not (Test-Path -LiteralPath $lootRoot -PathType Container)){continue}
    foreach($file in [IO.Directory]::GetFiles($lootRoot,'*.json',[IO.SearchOption]::AllDirectories)){
        if([IO.Path]::GetFullPath($file) -eq [IO.Path]::GetFullPath($stockFile)){continue}
        $sanitized=$file.Replace('\','/').Replace('//','/')
        $ignored=$false
        foreach($pattern in $order.aIgnorePatterns){
            if($null -ne $pattern -and $sanitized.Contains($pattern.Replace('\','/').Replace('//','/'))){$ignored=$true;break}
        }
        if($ignored){continue}
        $raw=[IO.File]::ReadAllText($file)
        if(-not $raw.Contains('SupplyKiosk')){continue}
        foreach($row in @(Read-Loot $raw)){
            if($row.strName -cmatch '^Itm(?:OKLGSupplyKiosk|SupplyKiosk[A-Z]*)Inv$'){$effective[$row.strName]=$row}
        }
    }
}
if($effective.Count -eq 0){throw 'No native Supply stock records found.'}
$result=@(foreach($id in @($effective.Keys | Sort-Object)){
    $row=$effective[$id]
    $loots=@($row.aLoots | Where-Object {$_ -and $_ -notmatch '^ItmOstrabusBottleRefillerStock='})
    if(-not $RemoveStock){$loots+='ItmOstrabusBottleRefillerStock=1.0x1'}
    $row | Add-Member -Force NoteProperty aLoots $loots
    $row
})
# Remain last even on uninstall, stripping stale copies of our stock out of
# another shop mod's previously composed rows while retaining that mod's goods.
$order.aLoadOrder=@($order.aLoadOrder | Where-Object {($_ -split '\|',2)[0] -ne $modName})+@($modName)
$utf8=[Text.UTF8Encoding]::new($false)
New-Item -ItemType Directory -Path (Split-Path -Parent $stockFile) -Force | Out-Null
[IO.File]::WriteAllText($stockFile,(ConvertTo-Json -InputObject $result -Depth 64),$utf8)
[IO.File]::WriteAllText($OrderPath,(ConvertTo-Json -InputObject $records -Depth 64),$utf8)
Write-Host "Composed $($result.Count) Supply stock records; future refiller stock enabled: $(-not $RemoveStock)."
