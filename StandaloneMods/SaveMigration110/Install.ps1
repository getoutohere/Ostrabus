[CmdletBinding()]
param(
    [string]$GameDirectory = $env:OSTRANAUTS_DIR,
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'
$support = Join-Path $PSScriptRoot 'Support'
if (-not (Test-Path -LiteralPath $support)) {
    $support = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'Build'
}
. (Join-Path $support 'Find-Game.ps1')
$gamePath = Find-Ostranauts -GameDirectory $GameDirectory
if (Get-Process -Name Ostranauts -ErrorAction SilentlyContinue) {
    throw 'Close Ostranauts before installing or removing the migration plugin.'
}
if (-not (Test-Path -LiteralPath (Join-Path $gamePath 'BepInEx\core\BepInEx.dll') -PathType Leaf)) {
    throw 'Install BepInEx 5 before this plugin.'
}
$pluginRoot = Join-Path $gamePath 'BepInEx\plugins\OstrabusSaveMigration110'
$target = Join-Path $pluginRoot 'Ostrabus.SaveMigration110.dll'
if ($Uninstall) {
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        Move-Item -LiteralPath $target -Destination ($target + '.disabled-' + [Guid]::NewGuid().ToString('N'))
    }
    Write-Host 'Migration plugin removed. Saved native map fields and migration history remain valid without it.'
    return
}
$expectedNative = 'A79A5081D9C94307A45FE1D0B55FF1AA1D0AF6EA8DFCE1F9ACC5961CF260EF07'
$native = Join-Path $gamePath 'Ostranauts_Data\Managed\Assembly-CSharp.dll'
if ((Get-FileHash -LiteralPath $native).Hash -ne $expectedNative) {
    throw 'This package supports only the reviewed Ostranauts 1.1.0.0 assembly.'
}
$source = Join-Path $PSScriptRoot 'Plugin\Ostrabus.SaveMigration110.dll'
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    $source = Join-Path $PSScriptRoot 'bin\Release\netstandard2.1\Ostrabus.SaveMigration110.dll'
}
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw 'Build the standalone Release project or use its packaged installer.'
}
$backup = $null
New-Item -ItemType Directory -Path $pluginRoot -Force | Out-Null
if (Test-Path -LiteralPath $target -PathType Leaf) {
    $backupRoot = Join-Path $pluginRoot 'Backups'
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $backup = Join-Path $backupRoot ([Guid]::NewGuid().ToString('N') + '.bak')
    Copy-Item -LiteralPath $target -Destination $backup
}
try {
    Copy-Item -LiteralPath $source -Destination $target -Force
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $target).Hash) {
        throw 'Migration DLL copy verification failed.'
    }
} catch {
    if ($backup) { Copy-Item -LiteralPath $backup -Destination $target -Force }
    elseif (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target }
    throw
}
Write-Host 'Installed optional migration plugin. It reports a read-only plan on load.'
Write-Host 'Use migrate110 plan, then migrate110 apply. Save normally; uninstall this plugin afterward if desired.'
