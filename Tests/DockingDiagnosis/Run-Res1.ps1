param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
Push-Location $root
$names = @('DOCK_STUDY','DOCK_STUDY_PATCH','DOCK_STUDY_GROUPS','DOCK_STUDY_EXPORT','DOCK_TEST')
$prior = @{}
foreach ($name in $names) { $prior[$name] = [Environment]::GetEnvironmentVariable($name); [Environment]::SetEnvironmentVariable($name, $null) }
try {
    $work = Join-Path $root 'obj/Res1DockingCheck'
    $fixtures = Join-Path $work 'fixtures'
    New-Item -ItemType Directory -Force $fixtures | Out-Null
    foreach ($file in Get-ChildItem (Join-Path $root 'Tests/Fixtures/Docking/Res1') -Filter '*.json.gz') {
        $input = [IO.File]::OpenRead($file.FullName)
        $gzip = New-Object IO.Compression.GZipStream($input, [IO.Compression.CompressionMode]::Decompress)
        $output = [IO.File]::Create((Join-Path $fixtures $file.BaseName))
        try { $gzip.CopyTo($output) } finally { $output.Dispose(); $gzip.Dispose(); $input.Dispose() }
    }
    dotnet build Tests/DockingDiagnosis/DockingDiagnosis.csproj -c $Configuration -o "$work/harness" --nologo -v:q
    if ($LASTEXITCODE) { throw 'Docking harness build failed' }
    dotnet build Tests/SaveJsonCompatibility/Host/MonoHost.csproj -c $Configuration --nologo -v:q
    if ($LASTEXITCODE) { throw 'Mono host build failed' }
    $hostExe = "Tests/SaveJsonCompatibility/Host/bin/$Configuration/net8.0/MonoHost.exe"
    $env:DOCK_STUDY = $fixtures
    $env:DOCK_STUDY_GROUPS = '1'
    foreach ($mode in @('vanilla','corrected')) {
        $env:DOCK_STUDY_PATCH = if ($mode -eq 'corrected') { '1' } else { '0' }
        $log = Join-Path $work "$mode.log"
        # Native Unity icall warnings are expected outside Unity; retain them in the log.
        $savedPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        & $hostExe "$work/harness/DockingDiagnosis.dll" *> $log
        $result = $LASTEXITCODE
        $ErrorActionPreference = $savedPreference
        if ($result -ne 0 -or !(Select-String -LiteralPath $log -SimpleMatch 'PASS saved fit study:' -Quiet)) {
            throw "$mode docking replay failed; see $log"
        }
        Get-Content $log | Select-String 'Passed |PASS saved fit study:'
    }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $prior[$name]) }
    Pop-Location
}
