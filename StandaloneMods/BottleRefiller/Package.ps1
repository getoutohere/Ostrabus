[CmdletBinding()]
param([string]$Destination,[switch]$NoArchive,[switch]$SkipTests)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if(-not $SkipTests){
    & (Join-Path $repo 'Tests/BottleRefillerCheck/Test-Art.ps1')
    & dotnet build (Join-Path $repo 'Tests/BottleRefillerCheck') -c Release --nologo -v quiet
    if($LASTEXITCODE -ne 0){throw 'Refiller test build failed.'}
    & dotnet run --project (Join-Path $repo 'Tests/SaveJsonCompatibility/Host') -c Release -- (Join-Path $repo 'Tests/BottleRefillerCheck/bin/Release/netstandard2.1/BottleRefillerCheck.dll')
    if($LASTEXITCODE -ne 0){throw 'Refiller native regression checks failed.'}
}
& dotnet build (Join-Path $PSScriptRoot 'BottleRefiller.csproj') -c Release --nologo -v quiet
if($LASTEXITCODE -ne 0){throw 'Refiller build failed.'}
if(-not $Destination){$Destination=Join-Path $repo ('dist/BottleRefiller-0.1.7-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))}
if(Test-Path -LiteralPath $Destination){throw 'Package destination already exists.'}
New-Item -ItemType Directory -Path (Join-Path $Destination 'Plugin'),(Join-Path $Destination 'Support') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Release/netstandard2.1/Ostrabus.BottleRefiller.dll') -Destination (Join-Path $Destination 'Plugin')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Assets') -Destination $Destination -Recurse
foreach($file in @('Install.ps1','Install.cmd','Uninstall.cmd','Update-Stock.ps1','README.md','VALIDATION.md')){
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $Destination
}
New-Item -ItemType Directory -Path (Join-Path $Destination 'Art') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Art/README.md') -Destination (Join-Path $Destination 'Art')
foreach($file in @('Find-Game.ps1','Install-NativeMod.ps1')){Copy-Item -LiteralPath (Join-Path $repo "Build/$file") -Destination (Join-Path $Destination 'Support')}
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $Destination
if(-not $NoArchive){Compress-Archive -Path (Join-Path $Destination '*') -DestinationPath ($Destination+'.zip')}
Write-Output $Destination
