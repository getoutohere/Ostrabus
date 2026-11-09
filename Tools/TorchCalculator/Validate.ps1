param([switch]$ValidateNative)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Push-Location $workspace
try {
    & dotnet build Tools/TorchCalculator -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Calculator validation build failed.' }
    & dotnet run --project Tools/TorchCalculator -c Release --no-build -- --browser-checks obj/torch-browser-checks.html
    if ($LASTEXITCODE -ne 0) { throw 'Calculator checks failed.' }
    if ($ValidateNative) {
        foreach ($project in @('Tests/SaveJsonCompatibility/Host', 'Tools/TorchNativeOracle')) {
            & dotnet build $project -c Release --nologo
            if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
        }
        & dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tools/TorchNativeOracle/bin/Release/netstandard2.1/TorchNativeOracle.dll
        if ($LASTEXITCODE -ne 0) { throw 'Native reactor validation failed.' }
    }
    Write-Host 'Open obj/torch-browser-checks.html to run browser parity and interaction checks.'
} finally { Pop-Location }
