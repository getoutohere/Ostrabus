[CmdletBinding()]
param([string]$GameDirectory = $env:OSTRANAUTS_DIR, [switch]$Uninstall)
$ErrorActionPreference = 'Stop'
$support = Join-Path $PSScriptRoot 'Support'
if (-not (Test-Path -LiteralPath $support)) {
    $support = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'Build'
}
. (Join-Path $support 'Find-Game.ps1')
$gamePath = Find-Ostranauts -GameDirectory $GameDirectory
if (Get-Process -Name Ostranauts -ErrorAction SilentlyContinue) { throw 'Close Ostranauts before installing or removing this plugin.' }
if (-not (Test-Path -LiteralPath (Join-Path $gamePath 'BepInEx\core\BepInEx.dll') -PathType Leaf)) {
    throw 'Install BepInEx 5 before this plugin.'
}
$pluginRoot = Join-Path $gamePath 'BepInEx\plugins\OstrabusStationMigration'
$target = Join-Path $pluginRoot 'Ostrabus.StationMigration.dll'
if ($Uninstall) {
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        Move-Item -LiteralPath $target -Destination ($target + '.disabled-' + [Guid]::NewGuid().ToString('N'))
    }
    Write-Host 'Station migration plugin removed. Previously saved kiosks, items and native completion flags remain valid without it.'
    return
}
$native = Join-Path $gamePath 'Ostranauts_Data\Managed\Assembly-CSharp.dll'
if ((Get-FileHash -LiteralPath $native).Hash -notin @(
    '967927E609D93E5233FE16260D61DE3CB4AA5D753B738ED8A6BA1DBA4CF9FDE0',
    '133A9CC94AD4F4D5DB951B65249B6CDF78F53063419D836B8871B92960EC3C37',
    'E9E7B8796423701C157EA5718CB407E6517AC0435EF947062C9A27464F4BC82F'
)) {
    throw 'This package supports only the reviewed Ostranauts 1.1.0.1, 1.1.0.2 and 1.1.0.3 assemblies.'
}
$source = Join-Path $PSScriptRoot 'Plugin\Ostrabus.StationMigration.dll'
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    $source = Join-Path $PSScriptRoot 'bin\Release\netstandard2.1\Ostrabus.StationMigration.dll'
}
if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw 'Build the standalone Release project or use the packaged installer.' }
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
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $target).Hash) { throw 'Migration DLL copy verification failed.' }
} catch {
    if ($backup) { Copy-Item -LiteralPath $backup -Destination $target -Force }
    elseif (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target }
    throw
}
Write-Host 'Installed standalone station migration. The Ostrabus plugin and vanilla asset files were not changed.'
Write-Host 'Load a pre-migration save, check the migration completion log, then save normally. Close the game and uninstall this plugin afterward if desired.'
