[CmdletBinding()]
param([double]$Minutes=15,[double]$IntervalSeconds=2,[string]$OutputDirectory=$PSScriptRoot)
$ErrorActionPreference='Stop'
$directory=$OutputDirectory
$path=Join-Path $directory 'NORMAL-16X.jsonl'
$stopFile=Join-Path $directory 'STOP'
$writer=New-Object IO.StreamWriter($path,$true,(New-Object Text.UTF8Encoding($false)))
$writer.AutoFlush=$true
$deadline=[DateTime]::UtcNow.AddMinutes($Minutes)
$since=0L; $session=$null; $generation=$null; $failures=0
$ids=@('J-V5P','O-X6P','O-APJH','O-D5GM','O-7N07','E-H6GR')
Write-Output "Normal orbit recording: $path; duration $Minutes wall minutes; Ctrl+C or create STOP to end."
try {
    while ([DateTime]::UtcNow -lt $deadline -and !(Test-Path -LiteralPath $stopFile)) {
        try {
            $excursion=Invoke-RestMethod ('http://127.0.0.1:18764/excursion?shipId=M-5N4&limit=50&since='+$since) -TimeoutSec 5
            if ($null -ne $generation -and ($generation -ne $excursion.generation -or $session -ne $excursion.data.session)) {
                throw 'Simulation/recording session changed; ending this experiment.'
            }
            $generation=$excursion.generation; $session=$excursion.data.session
            $since=[long]$excursion.data.nextSince
            $excursion.data.events=@($excursion.data.events | Where-Object { $_.kind -ne 'navDisplay' })
            $ships=foreach ($id in $ids) {
                try {
                    $reply=Invoke-RestMethod ('http://127.0.0.1:18764/ships/'+$id) -TimeoutSec 5
                    [ordered]@{id=$id;epoch=$reply.epoch;motion=$reply.data.navigation.currentMotion;
                        propulsion=$reply.data.navigation.propulsion;scanTarget=$reply.data.scanTarget;
                        dockLinks=$reply.data.savedDockLinks;navPlan=$reply.data.navigation.currentNavPlan}
                } catch { [ordered]@{id=$id;error=$_.Exception.Message} }
            }
            $orbital=Invoke-RestMethod http://127.0.0.1:18764/orbital -TimeoutSec 5
            $row=[ordered]@{utc=[DateTime]::UtcNow.ToString('O');excursion=$excursion;orbital=$orbital;ships=@($ships)}
            $writer.WriteLine(($row | ConvertTo-Json -Depth 35 -Compress))
            $failures=0
            if ($since -lt [long]$excursion.data.latestSequence) { continue }
        } catch {
            $failures++
            $writer.WriteLine((@{utc=[DateTime]::UtcNow.ToString('O');error=$_.Exception.Message;failures=$failures} | ConvertTo-Json -Compress))
            if ($_.Exception.Message -like 'Simulation/recording session changed*' -or $failures -ge 5) { break }
        }
        Start-Sleep -Milliseconds ([int]($IntervalSeconds*1000))
    }
} finally { $writer.Dispose(); Write-Output 'Normal orbit recording stopped.' }
