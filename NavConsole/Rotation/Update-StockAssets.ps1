[CmdletBinding()]
param([string]$GameDirectory=$env:OSTRANAUTS_DIR)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\..\Build\Find-Game.ps1')
$game=Find-Ostranauts -GameDirectory $GameDirectory
$source=Join-Path $game 'Ostranauts_Data\StreamingAssets\data\loot\loot.json'
$rows=Get-Content -LiteralPath $source -Raw|ConvertFrom-Json
$overrides=foreach($row in $rows){
    if($row.strName -eq 'ItmNavStationModsAll'){
        $row.aCOs=@($row.aCOs)+@('ItmOstrabusNavRotation=1.0x1')
        $row
    }elseif(@($row.aLoots|Where-Object {$_ -like 'ItmNavModDuhrt=*'}).Count -gt 0){
        $entry=$row.aLoots|Where-Object {$_ -like 'ItmNavModDuhrt=*'}|Select-Object -First 1
        $row.aLoots=@($row.aLoots)+@($entry.Replace('ItmNavModDuhrt=','ItmOstrabusNavRotation='))
        $row
    }
}
$destination=Join-Path $PSScriptRoot 'Assets\data\loot\rotation_stock.json'
[IO.File]::WriteAllText($destination,(ConvertTo-Json -InputObject @($overrides) -Depth 12)+[Environment]::NewLine,[Text.UTF8Encoding]::new($false))
Write-Output "Rebased $(@($overrides).Count) native stock/editor entries; existing entries retained."
