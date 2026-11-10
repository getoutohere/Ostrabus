[CmdletBinding()]
param(
    [ValidateSet('bugfixes','bugfixes-qol','bugfixes-performance','recommended','full','developer','empty')][string]$Preset = 'recommended',
    [string]$ProfilePath,
    [string[]]$Features,
    [string]$SaveProfile,
    [string]$GameDirectory = $env:OSTRANAUTS_DIR,
    [switch]$NoMenu,
    [switch]$PlanOnly
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. (Join-Path $repo 'Build\Find-Game.ps1')
function Invoke-Checked {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed ($LASTEXITCODE): $($Arguments -join ' ')" }
}
function Invoke-BuilderJson {
    param([string[]]$Arguments)
    $json = & dotnet $builderDll @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Custom builder rejected the selection.' }
    return ($json -join "`n" | ConvertFrom-Json)
}
function Write-Selection {
    param([object]$Selection, [string]$Path)
    $fullPath = [IO.Path]::GetFullPath($Path)
    New-Item -ItemType Directory -Path (Split-Path -Parent $fullPath) -Force | Out-Null
    [IO.File]::WriteAllText($fullPath, (ConvertTo-Json -InputObject $Selection -Depth 20), [Text.UTF8Encoding]::new($false))
}
$previousGame = $env:OSTRANAUTS_DIR
Push-Location $repo
try {
    $env:OSTRANAUTS_DIR = Find-Ostranauts -GameDirectory $GameDirectory
    Invoke-Checked -Arguments @('build','CustomBuilder/Tool','-c','Release','--nologo','-v','quiet')
    $builderDll = Join-Path $PSScriptRoot 'Tool\bin\Release\net8.0\CustomBuilder.dll'
    $catalog = Invoke-BuilderJson -Arguments @('list')
    $working = Join-Path $repo ('obj\CustomBuilder\build-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $working -Force | Out-Null
    $selectionPath = Join-Path $working 'profile.json'
    if ($ProfilePath) {
        $selection = Get-Content -LiteralPath $ProfilePath -Raw | ConvertFrom-Json
    } elseif ($PSBoundParameters.ContainsKey('Features')) {
        $selection = [pscustomobject]@{ schemaVersion=1; name='custom'; features=@($Features | ForEach-Object { $_ -split ',' } | Where-Object { $_ } | Sort-Object -Unique) }
    } else { $selection = Invoke-BuilderJson -Arguments @('preset','--name',$Preset) }
    $visible = @($catalog.features | Where-Object { -not $_.hidden } | Sort-Object category,title)
    $interactive = -not ($NoMenu -or $PlanOnly -or $ProfilePath -or $PSBoundParameters.ContainsKey('Features'))
    while ($true) {
        Write-Selection $selection $selectionPath
        $plan = Invoke-BuilderJson -Arguments @('plan','--profile',$selectionPath)
        if (-not $interactive) { break }
        Write-Host "`nOstrabus custom plugin - $($selection.name)" -ForegroundColor Cyan
        Write-Host '[x] selected   [+] required dependency   [ ] excluded'
        $category = ''
        for ($index=0; $index -lt $visible.Count; $index++) {
            $feature = $visible[$index]
            if ($category -ne $feature.category) { $category=$feature.category; Write-Host "`n$($category.ToUpperInvariant())" }
            $mark = if ($selection.features -contains $feature.id) { 'x' } elseif ($plan.included -contains $feature.id) { '+' } else { ' ' }
            $extra = if ($feature.experimental) { ' [experimental; not in presets]' } else { '' }
            Write-Host ('{0,2} [{1}] {2}{3}' -f ($index+1),$mark,$feature.title,$extra)
        }
        Write-Host "`n$(@($plan.included).Count) features / $(@($plan.patches).Count) patch classes / $($plan.tickCalls) frame calls."
        foreach ($reason in $plan.requiredBy.PSObject.Properties) { Write-Host "  + $($reason.Name): required by $($reason.Value -join ', ')" }
        Write-Host 'Number: toggle | p: preset | s: save profile | l: load profile | b: build | q: quit'
        $answer = Read-Host 'Selection'
        if ($answer -eq 'q') { return }
        if ($answer -eq 'b') { break }
        if ($answer -eq 'p') {
            Write-Host 'bugfixes, bugfixes-qol, bugfixes-performance, recommended, full, developer, empty'
            $presetName = Read-Host 'Preset'
            if ($presetName -in @('bugfixes','bugfixes-qol','bugfixes-performance','recommended','full','developer','empty')) { $selection=Invoke-BuilderJson -Arguments @('preset','--name',$presetName) }
            continue
        }
        if ($answer -eq 's') { $path=Read-Host 'Profile filename (use .local.json for private choices)'; if ($path) { Write-Selection $selection $path }; continue }
        if ($answer -eq 'l') { $path=Read-Host 'Profile filename'; if ($path) { $selection=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }; continue }
        $number = 0
        if ([int]::TryParse($answer,[ref]$number) -and $number -ge 1 -and $number -le $visible.Count) {
            $id=$visible[$number-1].id
            if ($selection.features -contains $id) { $selection.features=@($selection.features | Where-Object { $_ -ne $id }) }
            elseif ($plan.included -contains $id) { Write-Host 'Required by another selection; remove its dependents first.' -ForegroundColor Yellow }
            else { $selection.features=@($selection.features)+$id }
            $selection.name='custom'
        }
    }
    if ($SaveProfile) { Write-Selection $selection $SaveProfile }
    Write-Host "`nIncluded: $($plan.included -join ', ')"
    if ($PlanOnly) { Write-Host "Plan only; no plugin built. Selection: $selectionPath"; return }
    $generated = Join-Path $working 'generated'
    Invoke-Checked -Arguments @($builderDll,'generate','--profile',$selectionPath,'--output',$generated)
    Invoke-Checked -Arguments @('build',(Join-Path $generated 'Ostrabus.csproj'),'-c','Release','--nologo','-v','quiet')
    $dll = Join-Path $generated 'bin\Release\netstandard2.1\Ostrabus.dll'
    Invoke-Checked -Arguments @($builderDll,'finalize','--profile',$selectionPath,'--dll',$dll)
    Invoke-Checked -Arguments @('build','Tests/CustomBuilderCheck','-c','Release','--nologo','-v','quiet')
    Invoke-Checked -Arguments @('build','Tests/SaveJsonCompatibility/Host','-c','Release','--nologo','-v','quiet')
    # Run native hosts sequentially: their Mono runtime staging directory is shared.
    $nativeLog=Join-Path $working 'native-binding.log'
    & dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/CustomBuilderCheck/bin/Release/netstandard2.1/CustomBuilderCheck.dll $dll *> $nativeLog
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $nativeLog -Tail 45 | Write-Host; throw "Native Harmony binding failed. Log: $nativeLog" }
    Write-Host ((Select-String -LiteralPath $nativeLog -Pattern '^Custom patch binding:').Line)
    & (Join-Path $PSScriptRoot 'Package-Custom.ps1') -BuildDirectory $generated -ProfilePath $selectionPath
    Write-Host 'Normal bin/Debug output and Deploy-Mod.cmd were not modified.' -ForegroundColor Green
} finally { Pop-Location; $env:OSTRANAUTS_DIR=$previousGame }
