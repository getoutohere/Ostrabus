[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SaveArchive,
    [Parameter(Mandatory = $true)][string]$RepairedArchive,
    [string]$GameDirectory = $env:OSTRANAUTS_DIR,
    [switch]$SkipPluginInstall,
    [switch]$ArchiveOnly,
    [switch]$AllowRunningGame
)
$ErrorActionPreference = 'Stop'
if ($AllowRunningGame -and -not $ArchiveOnly) { throw 'Running-game permission requires archive-only application; DLL installation must wait for exit.' }
if ($ArchiveOnly -and $SkipPluginInstall) { throw 'Use ArchiveOnly or SkipPluginInstall, not both.' }
if (-not $AllowRunningGame -and (Get-Process -Name Ostranauts -ErrorAction SilentlyContinue)) {
    throw 'Close Ostranauts first; a loaded session could overwrite the repaired metadata.'
}
$save = (Resolve-Path -LiteralPath $SaveArchive).Path
$repair = (Resolve-Path -LiteralPath $RepairedArchive).Path
$report = Get-Content -LiteralPath ($repair + '.report.json') -Raw | ConvertFrom-Json
if ($report.superseded) { throw 'Prepared repair was superseded; use the corrected departure reconstruction.' }
$mercury = $report.repairKind -eq 'mercury-original-attachment-v1'
if ($report.repairKind -and -not $mercury) { throw 'Unknown native repair kind.' }
if (-not $report.originalsUnchanged -or -not $report.productionReaderAccepted -or
    $report.changedEntry -ne 'boneyard-turnover.state.json' -or $report.nativePayloadsHashVerified -lt 1) {
    throw 'The prepared copy lacks a successful repair report.'
}
if ($mercury -and (-not $report.exactNativeChangesVerified -or $report.changedNativePayloads -lt 1 -or
    $report.nativePoseRoundTripsVerified -ne $report.candidateIds.Count -or -not $report.requiredTurnoverSHA256)) {
    throw 'The Mercury copy lacks exact native change and pose verification.'
}
if ((Get-FileHash -LiteralPath $save).Hash -ne $report.currentSourceSHA256) {
    throw 'The current save changed after preparation. Regenerate the repair; do not overwrite newer progress.'
}
if ((Get-FileHash -LiteralPath $repair).Hash -ne $report.repairedSHA256) {
    throw 'The prepared copy no longer matches its verification report.'
}
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$built = Join-Path $repo 'StandaloneMods\BoneyardTurnover\bin\Release\netstandard2.1\Ostrabus.BoneyardTurnover.dll'
if ($mercury -and (Get-FileHash -LiteralPath $built).Hash -ne $report.requiredTurnoverSHA256) {
    throw 'The built turnover DLL differs from the verified Mercury candidate.'
}
if ($ArchiveOnly) {
    # Explicit operator authorization is required for an active-session archive
    # edit. This never writes DLLs or changes the loaded world. Source/copy hash
    # checks and the atomic backup/replacement remain mandatory.
} elseif ($SkipPluginInstall) {
    . (Join-Path $repo 'Build\Find-Game.ps1')
    $gamePath = Find-Ostranauts -GameDirectory $GameDirectory
    $installed = Join-Path $gamePath 'BepInEx\plugins\OstrabusBoneyardTurnover\Ostrabus.BoneyardTurnover.dll'
    $built = Join-Path $repo 'StandaloneMods\BoneyardTurnover\bin\Release\netstandard2.1\Ostrabus.BoneyardTurnover.dll'
    if (-not (Test-Path -LiteralPath $installed) -or (Get-FileHash -LiteralPath $installed).Hash -ne (Get-FileHash -LiteralPath $built).Hash) {
        throw 'The installed turnover DLL does not match the corrected build.'
    }
} else {
    & (Join-Path $repo 'StandaloneMods\BoneyardTurnover\Install.ps1') -GameDirectory $GameDirectory
}
$backup = $save + '.before-turnover-' + [Guid]::NewGuid().ToString('N') + '.bak'
$temporary = $save + '.turnover-' + [Guid]::NewGuid().ToString('N') + '.tmp'
Copy-Item -LiteralPath $repair -Destination $temporary
if ((Get-FileHash -LiteralPath $temporary).Hash -ne $report.repairedSHA256) {
    throw 'Staged copy verification failed; original save remains intact.'
}
# A final check immediately precedes the atomic replacement. The backup is
# created by File.Replace itself, rather than by overwriting the original first.
if (-not $AllowRunningGame -and (Get-Process -Name Ostranauts -ErrorAction SilentlyContinue)) { throw 'Game started during preparation; original save retained.' }
if ((Get-FileHash -LiteralPath $save).Hash -ne $report.currentSourceSHA256) { throw 'Save changed during preparation; original retained.' }
[IO.File]::Replace($temporary, $save, $backup)
if ((Get-FileHash -LiteralPath $save).Hash -ne $report.repairedSHA256 -or
    (Get-FileHash -LiteralPath $backup).Hash -ne $report.currentSourceSHA256) {
    throw 'Post-replacement verification failed; preserve the backup and inspect before loading.'
}
$receipt = [ordered]@{
    appliedUtc = [DateTime]::UtcNow.ToString('O')
    saveArchive = $save
    originalBackup = $backup
    installedSaveSHA256 = $report.repairedSHA256
    creditedHoursAtSave = $report.creditedHours
    candidates = $report.candidateIds.Count
    changedNativePayloads = $(if ($mercury) { $report.changedNativePayloads } else { 0 })
    pluginInstallationDeferred = [bool]$ArchiveOnly
    runningSessionUnchanged = [bool]$AllowRunningGame
    requiredTurnoverSHA256 = $report.requiredTurnoverSHA256
}
[IO.File]::WriteAllText(($repair + '.applied.json'), ($receipt | ConvertTo-Json -Depth 5))
$receipt | ConvertTo-Json -Depth 5
