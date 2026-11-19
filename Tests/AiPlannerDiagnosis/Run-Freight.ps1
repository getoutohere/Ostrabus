[CmdletBinding()]
param([string]$OutputDirectory = 'obj/FreightRegression', [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Push-Location $repo
try {
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    if (-not $SkipBuild) {
        foreach ($project in @('Ostrabus.csproj', 'Tests/SaveJsonCompatibility/Host', 'Tests/AiPlannerDiagnosis',
            'Tests/AiRendezvousCheck', 'Tests/OrbitalCheck', 'Tests/PlanetaryNavigationCheck', 'Tests/PatchBindingCheck',
            'Tests/AiRouteCompletionCheck')) {
            $configuration = if ($project -eq 'Ostrabus.csproj') { 'Debug' } else { 'Release' }
            & dotnet build $project -c $configuration --nologo -v quiet
            if ($LASTEXITCODE -ne 0) { throw "Build failed: $project. No stale harness will be executed." }
        }
    }
    $hostDll = 'Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll'
    $planner = 'Tests/AiPlannerDiagnosis/bin/Release/netstandard2.1/AiPlannerDiagnosis.dll'
    $orbital = 'Tests/OrbitalCheck/bin/Release/netstandard2.1/OrbitalCheck.dll'
    $results = [Collections.Generic.List[object]]::new()
    function Verify([string]$Name, [string[]]$Arguments, [string]$Marker) {
        $prior = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $lines = @(& dotnet $hostDll @Arguments 2>&1)
            $code = $LASTEXITCODE
        } finally { $ErrorActionPreference = $prior }
        $lines | Set-Content -LiteralPath (Join-Path $output ($Name + '.log'))
        $receipt = @($lines | Where-Object { "$_" -match $Marker })
        $passed = $code -eq 0 -and $receipt.Count -gt 0
        $results.Add([ordered]@{ name = $Name; passed = $passed; exit = $code; receipt = @($receipt | ForEach-Object { "$_" }) })
        ConvertTo-Json -InputObject @($results.ToArray()) -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'results.json')
        Write-Host "$Name : $passed"
    }
    Verify 'clearance' @($planner, 'freight-safety') '\d+ freight clearance, cache, native oracle and detour checks passed\.'
    Verify 'destruction' @($planner, 'freight-destruction') '\d+.*passed'
    foreach ($mode in @('lifecycle', 'traffic-conversion', 'region-cleanup', 'flight-handoff')) {
        Verify $mode @($planner, $mode) '\d+.*checks passed[.;]'
    }
    Verify 'rendezvous' @('Tests/AiRendezvousCheck/bin/Release/netstandard2.1/AiRendezvousCheck.dll') '\d+ native AI RCS/rendezvous/freight-destination checks passed\.'
    Verify 'completion' @('Tests/AiRouteCompletionCheck/bin/Release/netstandard2.1/AiRouteCompletionCheck.dll') '\d+.*passed'
    Verify 'bindings' @('Tests/PatchBindingCheck/bin/Release/netstandard2.1/PatchBindingCheck.dll', '--report', (Join-Path $output 'bindings.json')) '0 failed; 0 warnings\.'
    foreach ($mode in @('', 'timing', 'decisions', 'pass-handoff-study', 'economy', 'cargo-study', 'delivery-study')) {
        $name = if ($mode) { $mode } else { 'orbital' }
        $arguments = @($orbital)
        if ($mode) { $arguments += $mode }
        Verify $name $arguments '\d+.*(?:checks|assertions) passed[.:]'
    }
    foreach ($mode in @('', 'avoidance-repair', 'departure-replay')) {
        $name = if ($mode) { $mode } else { 'planetary' }
        $arguments = @('Tests/PlanetaryNavigationCheck/bin/Release/netstandard2.1/PlanetaryNavigationCheck.dll')
        if ($mode) { $arguments += $mode }
        Verify $name $arguments '\d+.*(?:checks|assertions) passed[.:]'
    }
    foreach ($file in Get-ChildItem Tests/Fixtures/Navigation/FreightCourses/*.json) {
        $fixture = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        foreach ($id in $fixture.ships.PSObject.Properties.Name) {
            $departure = if ($file.BaseName -eq 'roll') { 'snapshot' } else { 'departure' }
            $marker = '^\{"id":"' + [regex]::Escape($id) + '".*"arrived":true'
            Verify ($id + '-normal') @($planner, 'freight-journey', $file.FullName, $id, $departure) $marker
            foreach ($mode in @('sff', 'pass')) {
                Verify ($id + '-' + $mode) @($orbital, 'freight-native', $file.FullName, $id, $mode) $marker
            }
        }
    }
    $failed = @($results | Where-Object { -not $_.passed })
    if ($failed.Count) { throw "$($failed.Count) freight regression cases failed. See $output/results.json." }
    Write-Host "$($results.Count) freight regression cases passed, including 78 complete voyage replays."
} finally { Pop-Location }
