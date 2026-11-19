[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $repo
try {
    foreach ($project in @('Tests/ObituaryCheck', 'Tests/SaveJsonCompatibility/Host')) {
        & dotnet build $project -c Release --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $project; refusing stale harness." }
    }
    New-Item -ItemType Directory -Path 'obj/ObituaryCheck' -Force | Out-Null
    # Run this host sequentially with other bundled-Mono checks.
    & dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tests/ObituaryCheck/bin/Release/netstandard2.1/ObituaryCheck.dll *> obj/ObituaryCheck/native-check.log
    $result = $LASTEXITCODE
    Get-Content obj/ObituaryCheck/native-check.log -Tail 24
    if ($result -ne 0) { throw "Obituary native checks failed: $result" }
} finally { Pop-Location }
