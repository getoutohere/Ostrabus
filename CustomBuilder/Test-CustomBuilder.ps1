[CmdletBinding()]
param([string]$GameDirectory=$env:OSTRANAUTS_DIR,[string[]]$Case=@())
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
. (Join-Path $repo 'Build\Find-Game.ps1')
function Checked {
    param([string[]]$Arguments,[string]$Log)
    # Native stderr is diagnostic output, not the process result. PowerShell 5
    # otherwise converts harmless offline Unity warnings into terminating errors.
    $savedPreference=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        if ($Log) { & dotnet @Arguments *> $Log } else { & dotnet @Arguments }
        $code=$LASTEXITCODE
    } finally { $ErrorActionPreference=$savedPreference }
    if ($code -ne 0) {
        if ($Log) { Get-Content -LiteralPath $Log -Tail 45 | Write-Host }
        throw "Check failed ($code): $($Arguments -join ' '). Log: $Log"
    }
}
$previousGame=$env:OSTRANAUTS_DIR
Push-Location $repo
try {
    $env:OSTRANAUTS_DIR=Find-Ostranauts -GameDirectory $GameDirectory
    Checked -Arguments @('build','CustomBuilder/Tool','-c','Release','--nologo','-v','quiet')
    $tool=Join-Path $PSScriptRoot 'Tool\bin\Release\net8.0\CustomBuilder.dll'
    Checked -Arguments @($tool,'self-test')
    Checked -Arguments @('build','Tests/CustomBuilderCheck','-c','Release','--nologo','-v','quiet')
    Checked -Arguments @('build','Tests/SaveJsonCompatibility/Host','-c','Release','--nologo','-v','quiet')
    $run=Join-Path $repo ('obj\CustomBuilder\tests-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $run -Force | Out-Null
    $cases=[ordered]@{
        'orbital-velocity'=@('orbital-velocity');
        'orbital-velocity-simulation'=@('orbital-velocity','economy-sff','coast','passive-docking-attachment');
        'ai-avoidance'=@('ai-avoidance');
        'avoidance-collisions'=@('ai-avoidance','hull-collisions');
        'ai-rendezvous'=@('ai-rendezvous');
        'cargo-loading'=@('cargo-loading');
        'ai-rendezvous-navigation'=@('ai-rendezvous','ai-flight-controls','departure-waypoints','economy-sff');
        'mm-fairness'=@('mm-fairness');
        'mm-fairness-defense'=@('mm-fairness','pdc-defense');
        'nav-module-placement'=@('nav-module-placement');
        'course-controls'=@('course-controls');
        'wreck-severity'=@('wreck-severity');
        'single-port-docking'=@('single-port-docking');
        'single-port-docking-settings'=@('single-port-docking','pda-settings');
        'obituaries'=@('obituaries');
        'obituaries-inspection'=@('obituaries','inspection');
        'save-forward-compatibility'=@('save-forward-compatibility');
        'rotation-module'=@('rotation-module');
        'bounty-departure-fuel'=@('bounty-departure-fuel');
        'civilian-visits'=@('civilian-visits');
        'civilian-no-derelict-visits'=@('civilian-no-derelict-visits');
        'civilian-visit-policies'=@('civilian-no-derelict-visits','civilian-visits','civilian-asteroid-visits');
        'civilian-asteroid-visits'=@('civilian-asteroid-visits');
        'faction-encounters'=@('faction-encounters');
        'ai-flight-controls'=@('ai-flight-controls');
        'ai-crew-identities'=@('ai-crew-identities');
        'ai-crew-recruitment'=@('ai-crew-recruitment');
        'ai-crew-retirement'=@('ai-crew-retirement');
        'route-completion'=@('route-completion');
        'console-buffer'=@('console-buffer');
        'population-gender'=@('population-gender');
        'ai-patrol-priority'=@('ai-patrol-priority');
        'quick-pda-hack'=@('quick-pda-hack');
        'co2-scrubber-audio'=@('co2-scrubber-audio');
        'sff-navigation-destination'=@('sff-navigation-destination');
        'nav-budget'=@('navigation-targets'); 'ceres'=@('ceres-navigation');
        'local-frames'=@('local-navigation-frames');
        'local-frames-departure-budget'=@('local-navigation-frames','departure-waypoints','navigation-targets');
        'torch-cache'=@('torch-fuel-cache'); 'torch-fuel'=@('torch-fuel');
        'torch-momentum'=@('torch-momentum'); 'torch-tow'=@('torch-towing');
        'passive-docking-attachment'=@('passive-docking-attachment');
        door=@('door-autoclose'); range=@('proximity-range'); station=@('station-docked');
        outlines=@('hull-outlines'); collisions=@('hull-collisions');
        pda=@('pda-settings','crew-hotkeys'); orbit=@('orbital'); coast=@('coast');
        sff=@('sff'); economy=@('economy-sff'); commands=@('legacy-commands');
        social=@('social-filter'); json=@('save-json'); 'sensor-modules'=@('em-propagation','sensors');
        all=@((Get-Content CustomBuilder/features.json -Raw | ConvertFrom-Json).features.id)
    }
    foreach ($name in @('empty','bugfixes','bugfixes-qol','bugfixes-performance','recommended','full','developer')) {
        $file=Join-Path $run ($name+'.json')
        Checked -Arguments @($tool,'preset','--name',$name,'--output',$file)
        $cases[$name]=@((Get-Content -LiteralPath $file -Raw | ConvertFrom-Json).features)
    }
    foreach ($name in $Case) {
        if (-not $cases.Contains($name)) { throw "Unknown custom-builder test case: $name" }
    }
    $selectedCases=@($cases.Keys | Where-Object { $Case.Count -eq 0 -or $_ -in $Case })
    foreach ($name in $selectedCases) {
        $file=Join-Path $run ($name+'.json')
        [IO.File]::WriteAllText($file,(ConvertTo-Json -InputObject ([ordered]@{schemaVersion=1;name=$name;features=@($cases[$name])}) -Depth 10),[Text.UTF8Encoding]::new($false))
        $generated=Join-Path $run $name
        Checked -Arguments @($tool,'generate','--profile',$file,'--output',$generated) -Log (Join-Path $run ($name+'-generate.log'))
        Checked -Arguments @('build',(Join-Path $generated 'Ostrabus.csproj'),'-c','Release','--nologo','-v','quiet') -Log (Join-Path $run ($name+'-build.log'))
        $dll=Join-Path $generated 'bin\Release\netstandard2.1\Ostrabus.dll'
        Checked -Arguments @($tool,'finalize','--profile',$file,'--dll',$dll) -Log (Join-Path $run ($name+'-verify.log'))
        $before=(Get-FileHash -LiteralPath $dll).Hash
        Checked -Arguments @($tool,'finalize','--profile',$file,'--dll',$dll) -Log (Join-Path $run ($name+'-idempotent.log'))
        if ((Get-FileHash -LiteralPath $dll).Hash -ne $before) { throw 'Repeated finalization changed the assembly.' }
        $baseline=Join-Path $repo 'bin\Debug\netstandard2.1\Ostrabus.dll'
        if (Test-Path -LiteralPath $baseline) { Checked -Arguments @($tool,'compare-order','--profile',$file,'--dll',$dll,'--baseline',$baseline) -Log (Join-Path $run ($name+'-order.log')) }
        $nativeLog=Join-Path $run ($name+'-native.log')
        Checked -Arguments @('run','--project','Tests/SaveJsonCompatibility/Host','-c','Release','--no-build','--','Tests/CustomBuilderCheck/bin/Release/netstandard2.1/CustomBuilderCheck.dll',$dll) -Log $nativeLog
        Write-Host "$name : $((Select-String -LiteralPath $nativeLog -Pattern '^Custom patch binding:').Line)"
    }
    Write-Host "Custom builder: $($selectedCases.Count) compiled/verified/native-bound profiles passed. Logs: $run" -ForegroundColor Green
} finally { Pop-Location; $env:OSTRANAUTS_DIR=$previousGame }
