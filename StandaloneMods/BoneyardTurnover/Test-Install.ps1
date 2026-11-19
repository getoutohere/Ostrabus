[CmdletBinding()]
param([string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
if (-not $PackageDirectory) { $PackageDirectory = $PSScriptRoot }
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repo 'Build\Find-Game.ps1')
$real = Find-Ostranauts
$fixture = [IO.Path]::GetFullPath((Join-Path $repo ('obj\BoneyardInstaller-' + [Guid]::NewGuid().ToString('N'))))
$workspace = [IO.Path]::GetFullPath($repo).TrimEnd('\') + '\'
if (-not $fixture.StartsWith($workspace, [StringComparison]::OrdinalIgnoreCase)) { throw 'Installer fixture escaped workspace.' }
$checks = 0
function Check([bool]$Value, [string]$Reason) {
    $script:checks++
    if (-not $Value) { throw $Reason }
}
# The installer operates only on this disposable workspace tree. A running real
# game does not affect this fixture's files.
function Get-Process { param([string]$Name) return @() }
try {
    $managed = Join-Path $fixture 'Ostranauts_Data\Managed'
    $core = Join-Path $fixture 'BepInEx\core'
    $plugins = Join-Path $fixture 'BepInEx\plugins'
    $config = Join-Path $fixture 'BepInEx\config'
    New-Item -ItemType Directory -Path $managed,$core,$plugins,$config -Force | Out-Null
    $native = Join-Path $managed 'Assembly-CSharp.dll'
    Copy-Item -LiteralPath (Join-Path $real 'Ostranauts_Data\Managed\Assembly-CSharp.dll') -Destination $native
    [IO.File]::WriteAllText((Join-Path $core 'BepInEx.dll'), 'fixture-presence-only')
    $unrelated = Join-Path $plugins 'Ostrabus.dll'
    [IO.File]::WriteAllText($unrelated, 'unrelated-plugin-must-stay')
    $settings = Join-Path $config 'com.ostrabus.boneyardturnover.cfg'
    [IO.File]::WriteAllText($settings, '[Turnover] custom settings must stay')
    $unrelatedHash = (Get-FileHash -LiteralPath $unrelated).Hash
    $configHash = (Get-FileHash -LiteralPath $settings).Hash
    $nativeHash = (Get-FileHash -LiteralPath $native).Hash
    $installer = Join-Path $PackageDirectory 'Install.ps1'
    $root = Join-Path $plugins 'OstrabusBoneyardTurnover'
    $target = Join-Path $root 'Ostrabus.BoneyardTurnover.dll'
    $source = Join-Path $PackageDirectory 'Plugin\Ostrabus.BoneyardTurnover.dll'
    if (-not (Test-Path -LiteralPath $source)) { $source = Join-Path $PackageDirectory 'bin\Release\netstandard2.1\Ostrabus.BoneyardTurnover.dll' }
    & $installer -GameDirectory $fixture
    Check ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash -eq (Get-FileHash -LiteralPath $source).Hash) 'DLL copy/hash failed.'
    Check ((Get-FileHash -LiteralPath $unrelated).Hash -eq $unrelatedHash) 'Install changed main/custom plugin.'
    Check ((Get-FileHash -LiteralPath $native).Hash -eq $nativeHash) 'Install changed native assembly.'
    Check ((Get-FileHash -LiteralPath $settings).Hash -eq $configHash) 'Install replaced user configuration.'
    Check (-not (Test-Path -LiteralPath (Join-Path $fixture 'Ostranauts_Data\Mods'))) 'Install created asset overrides.'
    & $installer -GameDirectory $fixture
    $backups = @(Get-ChildItem -LiteralPath (Join-Path $root 'Backups') -Filter '*.bak')
    Check ($backups.Count -eq 1 -and (Get-FileHash -LiteralPath $backups[0].FullName).Hash -eq (Get-FileHash -LiteralPath $source).Hash) 'Upgrade lost previous DLL.'
    & $installer -GameDirectory $fixture -Uninstall
    Check (-not (Test-Path -LiteralPath $target)) 'Uninstall left active DLL.'
    Check (@(Get-ChildItem -LiteralPath $root -Filter '*.disabled-*').Count -eq 1) 'Uninstall lost reversible copy.'
    Check ((Get-FileHash -LiteralPath $unrelated).Hash -eq $unrelatedHash) 'Uninstall changed main/custom plugin.'
    Check ((Get-FileHash -LiteralPath $settings).Hash -eq $configHash) 'Uninstall removed user configuration.'
    & $installer -GameDirectory $fixture -Uninstall
    Check (-not (Test-Path -LiteralPath $target)) 'Repeated uninstall failed.'
    [IO.File]::WriteAllText($native, 'unsupported-native-fixture')
    $refused = $false
    try { & $installer -GameDirectory $fixture } catch { $refused = $_.Exception.Message -like '*reviewed Ostranauts*' }
    Check $refused 'Unsupported assembly was accepted.'
    Check (-not (Test-Path -LiteralPath $target)) 'Refused install created plugin.'
    Write-Output ($checks.ToString() + ' Boneyard Turnover installer checks passed; only the workspace fixture was written.')
} finally {
    # Recheck the absolute deletion target immediately before recursive cleanup.
    $resolved = [IO.Path]::GetFullPath($fixture)
    if (-not $resolved.StartsWith($workspace, [StringComparison]::OrdinalIgnoreCase) -or $resolved -eq $workspace.TrimEnd('\')) {
        throw 'Refusing unsafe fixture cleanup.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
