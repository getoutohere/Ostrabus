[CmdletBinding()]
param(
    [string]$ShipId = '',
    [string]$OutputPath = (Join-Path $PSScriptRoot ('Excursion-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.jsonl')),
    [ValidateRange(0.1, 30)][double]$IntervalSeconds = 0.5,
    [ValidateRange(0.01, 1440)][double]$DurationMinutes = 120,
    [ValidateRange(1, 65535)][int]$Port = 18764
)
$ErrorActionPreference = 'Stop'
$uri = 'http://127.0.0.1:' + $Port + '/excursion?limit=50'
if ($ShipId) { $uri += '&shipId=' + [Uri]::EscapeDataString($ShipId) }
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$writer = New-Object IO.StreamWriter($OutputPath, $true, (New-Object Text.UTF8Encoding($false)))
$writer.AutoFlush = $true
$since = 0L; $generation = $null; $session = $null; $failures = 0
$deadline = [DateTime]::UtcNow.AddMinutes($DurationMinutes)
Write-Host "Recording excursion to $OutputPath. Ctrl+C stops; game recording expires within 60 seconds."
try {
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            $reply = Invoke-RestMethod ($uri + '&since=' + $since) -TimeoutSec 5
            $writer.WriteLine(($reply | ConvertTo-Json -Depth 40 -Compress))
            $changed = $null -ne $session -and ($session -ne $reply.data.session -or $generation -ne $reply.generation)
            $session = $reply.data.session; $generation = $reply.generation
            if ($changed -or $reply.data.cursorAhead) { $since = 0L; continue }
            if ($reply.data.cursorOverrun) { Write-Warning 'Transition buffer overrun; missing events are marked in the log.' }
            $since = [long]$reply.data.nextSince
            $failures = 0
            # Drain outstanding events immediately, without waiting for the next sample.
            if ($since -lt [long]$reply.data.latestSequence) { continue }
        } catch {
            $failures++
            $writer.WriteLine((@{utc=[DateTime]::UtcNow.ToString('o');error=$_.Exception.Message;consecutiveFailures=$failures} | ConvertTo-Json -Compress))
            if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { throw 'Excursion endpoint is missing. Deploy the new DLL and restart the game.' }
            if ($failures -ge 10) { throw 'Stopped after ten consecutive inspection failures.' }
        }
        Start-Sleep -Milliseconds ([int]($IntervalSeconds * 1000))
    }
} finally { $writer.Dispose() }
