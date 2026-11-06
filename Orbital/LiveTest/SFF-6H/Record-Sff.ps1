[CmdletBinding()]
param([double]$Minutes=15,[string]$OutputDirectory=$PSScriptRoot)
$ErrorActionPreference='Stop'
$directory=$OutputDirectory
$stopFile=Join-Path $directory 'STOP'
$path=Join-Path $directory 'SFF.jsonl'
$writer=New-Object IO.StreamWriter($path,$true,(New-Object Text.UTF8Encoding($false)))
$writer.AutoFlush=$true
$deadline=[DateTime]::UtcNow.AddMinutes($Minutes)
$previous=$null; $lastJump=$null; $failures=0
Write-Output "SFF recorder active: $path"
try {
    while([DateTime]::UtcNow -lt $deadline -and !(Test-Path -LiteralPath $stopFile)) {
        try {
            $excursion=Invoke-RestMethod 'http://127.0.0.1:18764/excursion?shipId=M-5N4&limit=10' -TimeoutSec 30
            $orbital=Invoke-RestMethod 'http://127.0.0.1:18764/orbital' -TimeoutSec 30
            $player=Invoke-RestMethod 'http://127.0.0.1:18764/ships/O-GXD' -TimeoutSec 30
            $row=[ordered]@{utc=[DateTime]::UtcNow.ToString('O');excursion=$excursion;orbital=$orbital;player=$player}
            $writer.WriteLine(($row | ConvertTo-Json -Depth 40 -Compress))
            $jump=$orbital.data.lastSff.startEpoch
            if ($null -ne $jump -and $jump -ne $lastJump) {
                $stamp=([string]$jump).Replace('.','-')
                if ($null -ne $previous) { $previous | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath (Join-Path $directory ($stamp+'-BEFORE.json')) -Encoding utf8 }
                $row | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath (Join-Path $directory ($stamp+'-AFTER.json')) -Encoding utf8
                Write-Output "Captured SFF at $jump, skip $($orbital.data.lastSff.skipSeconds) game seconds."
                $lastJump=$jump
            }
            $previous=$row; $failures=0
        } catch {
            $failures++
            $writer.WriteLine((@{utc=[DateTime]::UtcNow.ToString('O');error=$_.Exception.Message} | ConvertTo-Json -Compress))
            if ($failures -ge 5) { break }
        }
        Start-Sleep -Seconds 2
    }
} finally { $writer.Dispose(); Write-Output 'SFF recorder stopped.' }
