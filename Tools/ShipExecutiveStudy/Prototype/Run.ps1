param()
$ErrorActionPreference = 'Stop'
$studyRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..')).Path
Push-Location -LiteralPath $studyRoot
try {
    if (-not (Test-Path -LiteralPath 'bin/Debug/netstandard2.1/Ostrabus.dll')) {
        throw 'Build the full Debug plugin first. This study will not rebuild or deploy production code.'
    }
    dotnet build "$PSScriptRoot/Prototype.csproj" -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Prototype build failed.' }
    dotnet build Tests/SaveJsonCompatibility/Host -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Offline Mono host build failed.' }
    dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- "$PSScriptRoot/bin/Release/netstandard2.1/Prototype.dll"
    if ($LASTEXITCODE -ne 0) { throw 'Prototype scenarios failed; inspect obj/ShipExecutivePrototype/results.json.' }
}
finally { Pop-Location }
