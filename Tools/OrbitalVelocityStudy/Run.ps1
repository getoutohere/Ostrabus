[CmdletBinding()]
param([string]$OutputDirectory = 'obj/OrbitalVelocityStudy/reproduction', [switch]$Quick, [switch]$Preserve, [switch]$Production)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $root
try {
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    [void][IO.Directory]::CreateDirectory($output)
    $hostDll = 'Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll'
    $prototype = 'Tools/OrbitalVelocityStudy/bin/Release/netstandard2.1/OrbitalVelocityStudy.dll'
    function Run-Mono([string[]]$Arguments,[string]$Log) {
        $previousPreference=$ErrorActionPreference
        try {
            # Expected negative controls write exceptions to stderr. Preserve
            # their process exit status instead of treating stderr as a PS error.
            $ErrorActionPreference='Continue'
            dotnet $hostDll @Arguments *> $Log
            return $LASTEXITCODE
        } finally { $ErrorActionPreference=$previousPreference }
    }
    $cases = @(
        @('OrbitalCheck','Tests/OrbitalCheck'),
        @('AiRendezvousCheck','Tests/AiRendezvousCheck'),
        @('AiRouteCompletionCheck','Tests/AiRouteCompletionCheck'),
        @('PlanetaryNavigationCheck','Tests/PlanetaryNavigationCheck'),
        @('DockingOrbitStudy','Tools/DockingOrbitStudy')
    )
    if (-not $Quick) { $cases += ,@('TorchNativeOracle','Tools/TorchNativeOracle') }
    foreach ($project in (@('Tests/SaveJsonCompatibility/Host','Tools/OrbitalVelocityStudy') + @($cases | ForEach-Object { $_[1] }))) {
        $name = Split-Path -Leaf $project
        dotnet build $project -c Release --nologo -v quiet *> (Join-Path $output ('build-' + $name + '.log'))
        if ($LASTEXITCODE -ne 0) { throw ('Build failed: ' + $project) }
    }
    # Every Mono host invocation is sequential: the host stages a shared runtime.
    $variantsExit=$null;$unadaptedExit=$null
    if ($Preserve -or $Production) {
        $studyMode=if($Production){'production'}else{'preserve'}
        $preserveExit=Run-Mono @($prototype,$studyMode,(Join-Path $output $studyMode)) (Join-Path $output ($studyMode+'.log'))
        if ($preserveExit -ne 0) { throw 'Position-preserving candidate failed its standalone gates.' }
    } else {
        $variantsExit=Run-Mono @($prototype,'run',(Join-Path $output 'variants')) (Join-Path $output 'variants.log')
        $preciseExit=Run-Mono @($prototype,'precise',(Join-Path $output 'precise')) (Join-Path $output 'precise.log')
        if ($preciseExit -ne 0) { throw 'Coherent precision variant failed its standalone gates.' }
        $unadaptedExit=Run-Mono @($prototype,'compose-precise-native',(Join-Path $output 'unadapted'),'Tests/OrbitalCheck/bin/Release/netstandard2.1/OrbitalCheck.dll') (Join-Path $output 'unadapted.log')
        if ($unadaptedExit -eq 0) { throw 'Expected native/detached precision mismatch was not reproduced.' }
    }
    $results = @()
    foreach ($case in $cases) {
        $name=$case[0];$assembly=$case[1]+'/bin/Release/netstandard2.1/'+$name+'.dll'
        $modes=if($Production){@('control','compose-production')}elseif($Preserve){@('control','compose-preserve')}else{@('control','compose','compose-precise')}
        foreach ($mode in $modes) {
            $log=Join-Path $output ($name+'-'+$mode+'.log')
            if ($mode -eq 'control') { $code=Run-Mono @($assembly) $log }
            else { $code=Run-Mono @($prototype,$mode,(Join-Path $output 'composition'),$assembly) $log }
            $results += [pscustomobject]@{suite=$name;mode=$mode;exitCode=$code;log=$log}
            $results | ConvertTo-Json | Set-Content (Join-Path $output 'composition.json')
            Write-Host ($name+' / '+$mode+': '+$code)
            if ($code -ne 0) { throw ('Composition failed: '+$name+' / '+$mode+'; inspect '+$log) }
        }
    }
    [ordered]@{completed=$true;initialVariantsExit=$variantsExit;unadaptedPrecisionExit=$unadaptedExit;
        composition=$results;productionImplementation=[bool]$Production;positionPreservingStudy=([bool]$Preserve -or [bool]$Production);
        unresolved=if($Production){'Production sources passed offline composition; Unity playtest remains. Native position-solver approximation deliberately retained.'}elseif($Preserve){'Offline position-preserving candidate passed; production integration and Unity playtest remain. Native position-solver approximation deliberately retained.'}else{'Precision changes existing derived body positions; no live/load continuity policy or Unity atmospheric validation exists.'}
    } | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $output 'verdict.json')
    Write-Host ('Offline study complete. This driver does not build or deploy the plugin. Results: '+$output)
} finally { Pop-Location }
