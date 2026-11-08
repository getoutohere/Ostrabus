param([string]$OutputDirectory = 'obj/sensor-comparison')
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Push-Location $workspace
try {
    foreach ($project in @('Tests/SaveJsonCompatibility/Host', 'Tools/SensorNativeOracle', 'Tools/SensorComparison')) {
        & dotnet build $project -c Release --nologo
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $project" }
    }
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    $baseline = Join-Path $OutputDirectory 'native-baseline.jsonl'
    & dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tools/SensorNativeOracle/bin/Release/netstandard2.1/SensorNativeOracle.dll $baseline
    if ($LASTEXITCODE -ne 0) { throw 'Native baseline failed.' }
    & dotnet run --project Tools/SensorComparison -c Release --no-build -- $baseline $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Sensor model checks/comparison failed.' }
} finally { Pop-Location }
