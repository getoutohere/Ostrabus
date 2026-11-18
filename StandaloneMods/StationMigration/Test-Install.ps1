[CmdletBinding()]
param([string]$PackageDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repo 'Build\Find-Game.ps1')
$real = Find-Ostranauts
$fixture = Join-Path $repo ('obj\StationMigrationInstaller-' + [Guid]::NewGuid().ToString('N'))
$fixture = [IO.Path]::GetFullPath($fixture)
if (-not $fixture.StartsWith(([IO.Path]::GetFullPath($repo) + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Installer fixture escaped the workspace.'
}
$managed = Join-Path $fixture 'Ostranauts_Data\Managed'
$core = Join-Path $fixture 'BepInEx\core'
$plugins = Join-Path $fixture 'BepInEx\plugins'
New-Item -ItemType Directory -Path $managed,$core,$plugins -Force | Out-Null
$native = Join-Path $managed 'Assembly-CSharp.dll'
Copy-Item -LiteralPath (Join-Path $real 'Ostranauts_Data\Managed\Assembly-CSharp.dll') -Destination $native
[IO.File]::WriteAllText((Join-Path $core 'BepInEx.dll'), 'fixture-presence-only')
$unrelated = Join-Path $plugins 'Ostrabus.dll'
[IO.File]::WriteAllText($unrelated, 'unrelated-plugin-must-stay')
$unrelatedHash = (Get-FileHash -LiteralPath $unrelated).Hash
$nativeHash = (Get-FileHash -LiteralPath $native).Hash
$checks = 0
function Check([bool]$Value, [string]$Reason) {
    $script:checks++
    if (-not $Value) { throw $Reason }
}
# This subprocess exercises only the explicit workspace fixture. A real game's
# process does not make copying files into that disposable tree unsafe.
function Get-Process { param([string]$Name) return @() }
$installer = Join-Path $PackageDirectory 'Install.ps1'
$root = Join-Path $plugins 'OstrabusStationMigration'
$target = Join-Path $root 'Ostrabus.StationMigration.dll'
$source = Join-Path $PackageDirectory 'Plugin\Ostrabus.StationMigration.dll'
if (-not (Test-Path -LiteralPath $source)) {
    $source = Join-Path $PackageDirectory 'bin\Release\netstandard2.1\Ostrabus.StationMigration.dll'
}
& $installer -GameDirectory $fixture
Check ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash -eq (Get-FileHash -LiteralPath $source).Hash) 'DLL copy/hash failed.'
Check ((Get-FileHash -LiteralPath $unrelated).Hash -eq $unrelatedHash) 'Install changed the main plugin.'
Check ((Get-FileHash -LiteralPath $native).Hash -eq $nativeHash) 'Install changed the native assembly.'
Check (-not (Test-Path -LiteralPath (Join-Path $fixture 'Ostranauts_Data\Mods'))) 'Install created native asset mods.'
& $installer -GameDirectory $fixture
$backups = @(Get-ChildItem -LiteralPath (Join-Path $root 'Backups') -Filter '*.bak')
Check ($backups.Count -eq 1 -and (Get-FileHash -LiteralPath $backups[0].FullName).Hash -eq (Get-FileHash -LiteralPath $source).Hash) 'Upgrade did not preserve the previous DLL.'
& $installer -GameDirectory $fixture -Uninstall
Check (-not (Test-Path -LiteralPath $target)) 'Uninstall left an active DLL.'
Check (@(Get-ChildItem -LiteralPath $root -Filter '*.disabled-*').Count -eq 1) 'Uninstall did not retain a reversible disabled copy.'
Check ((Get-FileHash -LiteralPath $unrelated).Hash -eq $unrelatedHash) 'Uninstall changed the main plugin.'
& $installer -GameDirectory $fixture -Uninstall
Check (-not (Test-Path -LiteralPath $target)) 'Repeated uninstall was not a no-op.'
[IO.File]::WriteAllText($native, 'unsupported-native-fixture')
$refused = $false
try { & $installer -GameDirectory $fixture }
catch { $refused = $_.Exception.Message -like '*reviewed Ostranauts*' }
Check $refused 'Installer accepted an unsupported assembly.'
Check (-not (Test-Path -LiteralPath $target)) 'Refused install created an active plugin.'
Write-Output ($checks.ToString() + ' standalone station migration installer checks passed; only the workspace fixture was written.')
