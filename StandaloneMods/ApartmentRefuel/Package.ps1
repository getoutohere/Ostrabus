[CmdletBinding()]
param([string]$Destination, [switch]$NoArchive)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
& dotnet build (Join-Path $PSScriptRoot 'ApartmentRefuel.csproj') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Standalone build failed.' }
if (-not $Destination) { $Destination = Join-Path $repo ('dist\ApartmentRefuel-0.1.0-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff')) }
if (Test-Path -LiteralPath $Destination) { throw 'Package destination already exists.' }
New-Item -ItemType Directory -Path (Join-Path $Destination 'Plugin') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $Destination 'Support') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin\Release\netstandard2.1\Ostrabus.ApartmentRefuel.dll') -Destination (Join-Path $Destination 'Plugin')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets') -Destination $Destination -Recurse
foreach ($file in @('Install.ps1','Install.cmd','Uninstall.cmd','README.md','DESIGN.md')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $Destination }
Copy-Item -LiteralPath (Join-Path $repo 'Tests\ApartmentRefuelCheck\README.md') -Destination (Join-Path $Destination 'VALIDATION.md')
$readmePath = Join-Path $Destination 'README.md'
$readme = [IO.File]::ReadAllText($readmePath).Replace('../../Tests/ApartmentRefuelCheck/README.md','VALIDATION.md')
[IO.File]::WriteAllText($readmePath,$readme,[Text.UTF8Encoding]::new($false))
foreach ($file in @('Find-Game.ps1','Install-NativeMod.ps1','Update-ApartmentRefuelStock.ps1')) { Copy-Item -LiteralPath (Join-Path $repo "Build\$file") -Destination (Join-Path $Destination 'Support') }
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $Destination
if (-not $NoArchive) { Compress-Archive -Path (Join-Path $Destination '*') -DestinationPath ($Destination+'.zip') }
Write-Output $Destination
