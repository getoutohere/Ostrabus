[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$OutputFile)
$ErrorActionPreference='Stop'
$ids=New-Object 'Collections.Generic.List[string]'
$offset=0
do {
    $page=Invoke-RestMethod ('http://127.0.0.1:18764/ships?limit=50&offset='+$offset) -TimeoutSec 10
    foreach($ship in $page.data.items) { $ids.Add($ship.id) }
    $next=$page.data.nextOffset
    if ($null -ne $next -and [int]$next -le $offset) { throw 'Registry cursor did not advance' }
    $offset=$next
} while($null -ne $next)
$items=New-Object 'Collections.Generic.List[object]'
$excursion=Invoke-RestMethod 'http://127.0.0.1:18764/excursion?limit=1' -TimeoutSec 10
$venus=$excursion.data.bodies.Venus
$km=149597870.7
foreach($id in $ids) {
    try {
        $brief=Invoke-RestMethod ('http://127.0.0.1:18764/ships/'+[uri]::EscapeDataString($id)+'/comms') -TimeoutSec 10
        $ship=$brief.data.ship
        $dx=($ship.position.xAU-$venus.xAU)*$km; $dy=($ship.position.yAU-$venus.yAU)*$km
        $distance=[Math]::Sqrt($dx*$dx+$dy*$dy)
        $detail=$null; $ai=$null
        if ($distance -le 1000000 -and !$ship.hiddenFromSystem) {
            $detail=Invoke-RestMethod ('http://127.0.0.1:18764/ships/'+[uri]::EscapeDataString($id)) -TimeoutSec 10
            if ($ship.isAI) { $ai=Invoke-RestMethod ('http://127.0.0.1:18764/ships/'+[uri]::EscapeDataString($id)+'/ai') -TimeoutSec 10 }
        }
        $items.Add([ordered]@{epoch=$brief.epoch;ship=$ship;distanceFromVenusKm=$distance;detail=$detail;ai=$ai})
    } catch { $items.Add([ordered]@{id=$id;error=$_.Exception.Message}) }
}
[ordered]@{utc=[DateTime]::UtcNow.ToString('O');generation=$excursion.generation;epoch=$excursion.epoch;
    venus=$venus;items=$items.ToArray();note='All registry briefs; full ship/AI details for visible objects within 1 million km of Venus. No game state changed.'} |
    ConvertTo-Json -Depth 45 | Set-Content -LiteralPath $OutputFile -Encoding utf8
Write-Output "Registry snapshot: $($items.Count) objects -> $OutputFile"
