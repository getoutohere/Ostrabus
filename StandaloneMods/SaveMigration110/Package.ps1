[CmdletBinding()]
param([string]$Destination, [switch]$NoArchive)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
& dotnet build (Join-Path $PSScriptRoot 'SaveMigration110.csproj') -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw 'Migration plugin build failed.' }
if (-not $Destination) {
    $Destination = Join-Path $repo ('dist\SaveMigration110-0.1.0-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}
if (Test-Path -LiteralPath $Destination) { throw 'Package destination already exists.' }
New-Item -ItemType Directory -Path (Join-Path $Destination 'Plugin') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $Destination 'Support') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin\Release\netstandard2.1\Ostrabus.SaveMigration110.dll') -Destination (Join-Path $Destination 'Plugin')
foreach ($file in @('Install.ps1','Install.cmd','Uninstall.cmd','README.md','FINDINGS.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $Destination
}
Copy-Item -LiteralPath (Join-Path $repo 'Build\Find-Game.ps1') -Destination (Join-Path $Destination 'Support')
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $Destination
if (-not $NoArchive) {
    Compress-Archive -Path (Join-Path $Destination '*') -DestinationPath ($Destination + '.zip')
}
Write-Output $Destination
