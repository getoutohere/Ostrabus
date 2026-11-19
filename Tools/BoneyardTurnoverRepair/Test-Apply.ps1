$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repo 'Build\Find-Game.ps1')
$game = Find-Ostranauts
$fixture = [IO.Path]::GetFullPath((Join-Path $repo ('obj\TurnoverApplyCheck-' + [Guid]::NewGuid().ToString('N'))))
$workspace = [IO.Path]::GetFullPath($repo).TrimEnd('\') + '\'
if (-not $fixture.StartsWith($workspace, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture escaped workspace.' }
function Get-Process { param([string]$Name) return @() }
$checks = 0
function Check([bool]$Value, [string]$Message) {
    $script:checks++
    if (-not $Value) { throw $Message }
}
try {
    $fakeGame = Join-Path $fixture 'Game'
    $managed = Join-Path $fakeGame 'Ostranauts_Data\Managed'
    $core = Join-Path $fakeGame 'BepInEx\core'
    New-Item -ItemType Directory -Path $managed,$core -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $game 'Ostranauts_Data\Managed\Assembly-CSharp.dll') -Destination $managed
    [IO.File]::WriteAllText((Join-Path $core 'BepInEx.dll'), 'fixture-presence-only')
    $save = Join-Path $fixture 'save.zip'
    $repair = Join-Path $fixture 'repair.zip'
    [IO.File]::WriteAllText($save, 'original-save-fixture')
    [IO.File]::WriteAllText($repair, 'repaired-save-fixture')
    $originalHash = (Get-FileHash -LiteralPath $save).Hash
    $repairHash = (Get-FileHash -LiteralPath $repair).Hash
    $report = [ordered]@{
        originalsUnchanged = $true
        productionReaderAccepted = $true
        changedEntry = 'boneyard-turnover.state.json'
        nativePayloadsHashVerified = 1
        currentSourceSHA256 = $originalHash
        repairedSHA256 = $repairHash
        creditedHours = 12.5
        candidateIds = @('fixture-wreck')
    }
    [IO.File]::WriteAllText(($repair + '.report.json'), ($report | ConvertTo-Json))
    $report['superseded'] = $true
    [IO.File]::WriteAllText(($repair + '.report.json'), ($report | ConvertTo-Json))
    $refused = $false
    try { & (Join-Path $PSScriptRoot 'Apply-Repair.ps1') -SaveArchive $save -RepairedArchive $repair -GameDirectory $fakeGame }
    catch { $refused = $_.Exception.Message -like '*repair was superseded*' }
    Check $refused 'Superseded repair was accepted.'
    $report['superseded'] = $false
    [IO.File]::WriteAllText(($repair + '.report.json'), ($report | ConvertTo-Json))
    [IO.File]::WriteAllText($save, 'newer-player-progress')
    $refused = $false
    try { & (Join-Path $PSScriptRoot 'Apply-Repair.ps1') -SaveArchive $save -RepairedArchive $repair -GameDirectory $fakeGame }
    catch { $refused = $_.Exception.Message -like '*save changed after preparation*' }
    Check $refused 'Newer progress was not rejected.'
    Check ([IO.File]::ReadAllText($save) -eq 'newer-player-progress') 'Refusal modified newer progress.'
    Check (-not (Test-Path (Join-Path $fakeGame 'BepInEx\plugins'))) 'Refusal installed a plugin.'
    [IO.File]::WriteAllText($save, 'original-save-fixture')
    $report['repairKind'] = 'mercury-original-attachment-v1'
    [IO.File]::WriteAllText(($repair + '.report.json'), ($report | ConvertTo-Json))
    $refused = $false
    try { & (Join-Path $PSScriptRoot 'Apply-Repair.ps1') -SaveArchive $save -RepairedArchive $repair -GameDirectory $fakeGame }
    catch { $refused = $_.Exception.Message -like '*Mercury copy lacks exact native change*' }
    Check $refused 'Mercury report without exact native verification was accepted.'
    $report.Remove('repairKind')
    [IO.File]::WriteAllText(($repair + '.report.json'), ($report | ConvertTo-Json))
    & (Join-Path $PSScriptRoot 'Apply-Repair.ps1') -SaveArchive $save -RepairedArchive $repair -GameDirectory $fakeGame | Out-Null
    Check ((Get-FileHash -LiteralPath $save).Hash -eq $repairHash) 'Installed save differs from prepared copy.'
    $backups = @(Get-ChildItem -LiteralPath $fixture -Filter '*.bak')
    Check ($backups.Count -eq 1 -and (Get-FileHash -LiteralPath $backups[0].FullName).Hash -eq $originalHash) 'Original backup is missing or changed.'
    Check (Test-Path -LiteralPath ($repair + '.applied.json')) 'Verified application receipt missing.'
    Check ((Get-FileHash -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll')).Hash -eq
        (Get-FileHash -LiteralPath (Join-Path $game 'Ostranauts_Data\Managed\Assembly-CSharp.dll')).Hash) 'Native assembly changed.'
    $refused = $false
    try { & (Join-Path $PSScriptRoot 'Apply-Repair.ps1') -SaveArchive $save -RepairedArchive $repair -GameDirectory $fakeGame }
    catch { $refused = $_.Exception.Message -like '*save changed after preparation*' }
    Check $refused 'Repeated application was not rejected.'
    # Exercise the separate native-repair receipt and DLL identity guards in
    # this disposable fixture; the fake payload is never a real save.
    [IO.File]::WriteAllText($save, 'original-save-fixture')
    $report['repairKind'] = 'mercury-original-attachment-v1'
    $report['exactNativeChangesVerified'] = $true
    $report['changedNativePayloads'] = 5
    $report['nativePoseRoundTripsVerified'] = 1
    $report['requiredTurnoverSHA256'] = 'wrong-candidate'
    [IO.File]::WriteAllText(($repair + '.report.json'), ($report | ConvertTo-Json))
    $refused = $false
    try { & (Join-Path $PSScriptRoot 'Apply-Repair.ps1') -SaveArchive $save -RepairedArchive $repair -GameDirectory $fakeGame }
    catch { $refused = $_.Exception.Message -like '*built turnover DLL differs*' }
    Check $refused 'Wrong Mercury DLL identity was accepted.'
    $built = Join-Path $repo 'StandaloneMods\BoneyardTurnover\bin\Release\netstandard2.1\Ostrabus.BoneyardTurnover.dll'
    $report['requiredTurnoverSHA256'] = (Get-FileHash -LiteralPath $built).Hash
    [IO.File]::WriteAllText(($repair + '.report.json'), ($report | ConvertTo-Json))
    & (Join-Path $PSScriptRoot 'Apply-Repair.ps1') -SaveArchive $save -RepairedArchive $repair -GameDirectory $fakeGame -SkipPluginInstall | Out-Null
    $nativeReceipt = Get-Content -LiteralPath ($repair + '.applied.json') -Raw | ConvertFrom-Json
    Check ($nativeReceipt.changedNativePayloads -eq 5) 'Native repair receipt incorrectly claimed metadata-only changes.'
    Check ((Get-FileHash -LiteralPath $save).Hash -eq $repairHash) 'Native repair fixture did not install the verified copy.'
    [IO.File]::WriteAllText($save, 'original-save-fixture')
    function Get-Process { param([string]$Name) return [PSCustomObject]@{ ProcessName = 'Ostranauts' } }
    $refused = $false
    try { & (Join-Path $PSScriptRoot 'Apply-Repair.ps1') -SaveArchive $save -RepairedArchive $repair -GameDirectory $fakeGame -ArchiveOnly }
    catch { $refused = $_.Exception.Message -like 'Close Ostranauts first*' }
    Check $refused 'An open game was accepted without explicit running-game authorization.'
    $refused = $false
    try { & (Join-Path $PSScriptRoot 'Apply-Repair.ps1') -SaveArchive $save -RepairedArchive $repair -GameDirectory $fakeGame -AllowRunningGame }
    catch { $refused = $_.Exception.Message -like '*requires archive-only*' }
    Check $refused 'Running-game authorization permitted DLL installation.'
    $installedFixture = Join-Path $fakeGame 'BepInEx\plugins\OstrabusBoneyardTurnover\Ostrabus.BoneyardTurnover.dll'
    $fixtureDllHash = (Get-FileHash -LiteralPath $installedFixture).Hash
    & (Join-Path $PSScriptRoot 'Apply-Repair.ps1') -SaveArchive $save -RepairedArchive $repair -GameDirectory $fakeGame -ArchiveOnly -AllowRunningGame | Out-Null
    $archiveReceipt = Get-Content -LiteralPath ($repair + '.applied.json') -Raw | ConvertFrom-Json
    Check ($archiveReceipt.pluginInstallationDeferred -and $archiveReceipt.runningSessionUnchanged) 'Archive-only receipt did not defer installation and preserve the running session.'
    Check ((Get-FileHash -LiteralPath $installedFixture).Hash -eq $fixtureDllHash) 'Archive-only mode changed a plugin.'
    Check ((Get-FileHash -LiteralPath $save).Hash -eq $repairHash) 'Archive-only mode did not apply the verified copy.'
    Write-Output ($checks.ToString() + ' apply checks passed; only the workspace fixture was modified.')
} finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (-not $resolved.StartsWith($workspace, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing unsafe fixture cleanup.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
