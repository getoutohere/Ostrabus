[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$BuildDirectory, [Parameter(Mandatory=$true)][string]$ProfilePath)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$buildPath=[IO.Path]::GetFullPath($BuildDirectory)
$plan=Get-Content -LiteralPath (Join-Path $buildPath 'selection.json') -Raw | ConvertFrom-Json
$builderDll=Join-Path $PSScriptRoot 'Tool\bin\Release\net8.0\CustomBuilder.dll'
$dll=Join-Path $buildPath 'bin\Release\netstandard2.1\Ostrabus.dll'
& dotnet $builderDll verify --profile $ProfilePath --dll $dll --root $repo
if ($LASTEXITCODE -ne 0) { throw 'Assembly selection verification failed; refusing to package.' }
$safeName=$plan.name -replace '[^a-zA-Z0-9_-]','-'
if (-not $safeName) { $safeName='custom' }
$packageName='Ostrabus-Custom-{0}-{1}-{2}' -f $safeName,$plan.fingerprint.Substring(0,12),[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff')
$destination=Join-Path $repo ('dist\'+$packageName)
if (Test-Path -LiteralPath $destination) { throw 'Package destination already exists.' }
New-Item -ItemType Directory -Path (Join-Path $destination 'Plugin') -Force | Out-Null
foreach ($name in @('Ostrabus.dll','Ostrabus.pdb')) { Copy-Item -LiteralPath (Join-Path (Split-Path -Parent $dll) $name) -Destination (Join-Path $destination 'Plugin') }
Copy-Item -LiteralPath (Join-Path $buildPath 'selection.json') -Destination $destination
Copy-Item -LiteralPath $ProfilePath -Destination (Join-Path $destination 'profile.json')
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $destination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-Custom.ps1') -Destination $destination
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-Custom.cmd') -Destination $destination
New-Item -ItemType Directory -Path (Join-Path $destination 'Support') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'Build\Find-Game.ps1') -Destination (Join-Path $destination 'Support')
if ($plan.pdaAssets) {
    New-Item -ItemType Directory -Path (Join-Path $destination 'PdaSettings') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo 'PdaSettings\Assets') -Destination (Join-Path $destination 'PdaSettings') -Recurse
    Copy-Item -LiteralPath (Join-Path $repo 'PdaSettings\Install-Assets.ps1') -Destination (Join-Path $destination 'Support')
}
$hashes=[ordered]@{}
foreach ($name in @('Ostrabus.dll','Ostrabus.pdb')) { $hashes[$name]=(Get-FileHash -LiteralPath (Join-Path $destination ('Plugin\'+$name)) -Algorithm SHA256).Hash.ToLowerInvariant() }
$manifest=[ordered]@{schemaVersion=1; kind='Ostrabus custom build'; selectionHash=$plan.fingerprint; pluginFiles=$hashes}
[IO.File]::WriteAllText((Join-Path $destination 'package.json'),(ConvertTo-Json -InputObject $manifest -Depth 20),[Text.UTF8Encoding]::new($false))
$readme=@"
Ostrabus custom build: $($plan.name)

Close Ostranauts, then run Install-Custom.cmd (requires BepInEx 5).
For a nonstandard installation, pass -GameDirectory to Install-Custom.ps1
or set OSTRANAUTS_DIR. Administrator rights may be needed for Program Files.
This replaces the one installed Ostrabus plugin; do not install two variants.
Restart the game after changing profiles. Existing BepInEx settings are retained.

Included features: $($plan.included -join ', ')
See selection.json for dependencies and profile.json to reproduce this selection.
Included features retain their normal configuration defaults; selecting a feature
does not force its optional setting on. The PDA shows only included controls.
Inactive shared support code may remain in the DLL, without automatic registration.
Normal maintainer Deploy-Mod.cmd continues to install the full normal build.

Target: Ostranauts 1.0.1.10 / BepInEx 5. Game binaries are not redistributed.
Torch autopilot work pending playtesting is not included in custom builds.
"@
[IO.File]::WriteAllText((Join-Path $destination 'README.txt'),$readme,[Text.UTF8Encoding]::new($false))
Compress-Archive -LiteralPath $destination -DestinationPath ($destination+'.zip')
Write-Host "Custom package: $destination.zip" -ForegroundColor Green
