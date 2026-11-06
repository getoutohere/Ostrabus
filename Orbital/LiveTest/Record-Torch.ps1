[CmdletBinding()]
param([double]$Minutes=60,[double]$IntervalSeconds=5,[string]$ShipId='O-GXD',[string]$TargetId='')
$ErrorActionPreference='Stop'
$directory=Join-Path $PSScriptRoot ('TORCH-PASSIVE-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$writer=New-Object IO.StreamWriter((Join-Path $directory 'READINGS.jsonl'),$false,(New-Object Text.UTF8Encoding($false)))
$writer.AutoFlush=$true
$deadline=[DateTime]::UtcNow.AddMinutes($Minutes)
$generation=$null; $failures=0; $since=0L; $lastSff=$null
Write-Output "Passive torch recording: $directory; every $IntervalSeconds seconds for up to $Minutes wall minutes."
try {
    $recent=Invoke-RestMethod 'http://127.0.0.1:18764/logs?limit=1' -TimeoutSec 10
    $since=[long]$recent.latestSequence
    while([DateTime]::UtcNow -lt $deadline -and !(Test-Path -LiteralPath (Join-Path $directory 'STOP'))) {
        try {
            $ship=Invoke-RestMethod ('http://127.0.0.1:18764/ships/'+$ShipId) -TimeoutSec 30
            if ($null -ne $generation -and $ship.generation -ne $generation) { Write-Output 'Simulation changed; recorder stopped.'; break }
            $generation=$ship.generation
            $orbital=Invoke-RestMethod 'http://127.0.0.1:18764/orbital' -TimeoutSec 30
            $target=$null
            if ($TargetId) {
                try {
                    $reply=Invoke-RestMethod ('http://127.0.0.1:18764/ships/'+[uri]::EscapeDataString($TargetId)) -TimeoutSec 10
                    $target=[ordered]@{id=$TargetId;epoch=$reply.epoch;position=$reply.data.position;motion=$reply.data.navigation.currentMotion}
                } catch { $target=[ordered]@{id=$TargetId;error=$_.Exception.Message} }
            }
            $logEntries=New-Object 'Collections.Generic.List[object]'
            $overrun=$false; $caughtUp=$false
            for($page=0;$page -lt 4;$page++) {
                $logs=Invoke-RestMethod ('http://127.0.0.1:18764/logs?limit=50&since='+$since) -TimeoutSec 10
                foreach($entry in $logs.items) { if ($entry.level -match 'Error|Warn|Exception|Assert') { $logEntries.Add($entry) } }
                $overrun=$overrun -or $logs.cursorOverrun
                $since=[long]$logs.nextSince
                if ($since -ge [long]$logs.latestSequence) { $caughtUp=$true; break }
            }
            $plan=$ship.data.navigation.currentNavPlan
            $jump=$orbital.data.lastSff.startEpoch
            $newJump=$null
            if ($null -ne $jump -and $jump -ne $lastSff) { $newJump=$orbital.data.lastSff; $lastSff=$jump }
            $row=[ordered]@{utc=[DateTime]::UtcNow.ToString('O');generation=$generation;epoch=$ship.epoch;shipId=$ShipId;
                inspectionMs=$ship.inspectionMs;loadState=$ship.data.loadState;motion=$ship.data.navigation.currentMotion;
                scanTarget=$ship.data.scanTarget;targetData=$ship.data.navigation.targetData;destination=$target;
                navPlan=$plan;propulsion=$ship.data.navigation.propulsion;dockLinks=$ship.data.savedDockLinks;
                orbitalEpoch=$orbital.epoch;orbitalEnabled=$orbital.data.enabled;coast=$orbital.data.coast;
                sffEnabled=$orbital.data.sffEnabled;newSffReport=$newJump;
                warningsAndErrors=$logEntries.ToArray();logCursorOverrun=$overrun;logCursorCaughtUp=$caughtUp}
            $writer.WriteLine(($row | ConvertTo-Json -Depth 40 -Compress))
            $failures=0
        } catch {
            $failures++
            $writer.WriteLine((@{utc=[DateTime]::UtcNow.ToString('O');error=$_.Exception.Message} | ConvertTo-Json -Compress))
            if ($failures -ge 5) { break }
        }
        Start-Sleep -Milliseconds ([int]($IntervalSeconds*1000))
    }
} finally { $writer.Dispose(); Write-Output 'Passive torch recorder stopped.' }
