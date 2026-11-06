[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
$packageBuild = Join-Path $PSScriptRoot "bin\$Configuration\netstandard2.1"
foreach ($packageFile in @('Ostrabus.dll', 'Ostrabus.pdb', 'PdaSettings\Assets\mod_info.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path $packageBuild $packageFile) -PathType Leaf)) { throw "Build $Configuration before packaging: missing $packageFile" }
}
# Use a fresh directory for every package; never recursively delete a computed path.
$packageRoot = Join-Path $PSScriptRoot ('dist\Ostrabus-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path (Join-Path $packageRoot 'Plugin\PdaSettings') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $packageRoot 'PdaSettings') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $packageRoot 'Build') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Build\Find-Game.ps1') -Destination (Join-Path $packageRoot 'Build')
foreach ($packageFile in @('Ostrabus.dll', 'Ostrabus.pdb')) { Copy-Item -LiteralPath (Join-Path $packageBuild $packageFile) -Destination (Join-Path $packageRoot 'Plugin') }
Copy-Item -LiteralPath (Join-Path $packageBuild 'PdaSettings\Assets') -Destination (Join-Path $packageRoot 'Plugin\PdaSettings') -Recurse
foreach ($packageFile in @('Deploy-Mod.ps1', 'Deploy-Mod.cmd')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $packageFile) -Destination $packageRoot }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PdaSettings\Install-Assets.ps1') -Destination (Join-Path $packageRoot 'PdaSettings')
foreach ($packageDoc in @('README.md', 'DESIGN.md', 'IMPLEMENTATION.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('PdaSettings\' + $packageDoc)) -Destination (Join-Path $packageRoot 'PdaSettings')
}
@'
Close Ostranauts, then double-click Deploy-Mod.cmd to install the plugin and PDA
assets. The installer retains other native mods and their enabled/disabled states.
Existing TemplateExample plugin copies are disabled with backups retained.
Restart the game and open MOD SETTINGS from the PDA home screen.

Saved controls persist to BepInEx config. Session controls reset on game restart.
The existing com.ostranauts.template.cfg filename is retained for compatibility.
To remove the app, disable Ostrabus Settings in the native Mods screen. Removing
the BepInEx plugin also removes the code fixes; existing saves need no conversion.
'@ | Set-Content -LiteralPath (Join-Path $packageRoot 'README.txt') -Encoding UTF8
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath ($packageRoot + '.zip')
Write-Output ($packageRoot + '.zip')
