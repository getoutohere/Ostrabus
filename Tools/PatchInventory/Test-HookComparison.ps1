[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ReportPath,
    [Parameter(Mandatory=$true)][string]$ManagedDirectory
)
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$toolPath=Join-Path $PSScriptRoot 'bin/Release/net8.0/PatchInventory.dll'
$ReportPath=[IO.Path]::GetFullPath($ReportPath)
$ManagedDirectory=[IO.Path]::GetFullPath($ManagedDirectory)
$runPath=Join-Path $repoRoot ('obj/PatchInventoryChecks/'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($runPath)|Out-Null
function Write-Receipt($receipt,[string]$name) {
    $path=Join-Path $runPath ($name+'.json')
    [IO.File]::WriteAllText($path,($receipt|ConvertTo-Json -Depth 40),[Text.UTF8Encoding]::new($false))
    return $path
}
function Compare-Receipt([string]$current,[string]$name,[bool]$expectFailure=$false) {
    $output=Join-Path $runPath ($name+'-result.json')
    $log=Join-Path $runPath ($name+'.log')
    $savedPreference=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        & dotnet $toolPath compare-hooks $ReportPath $current $ManagedDirectory $ManagedDirectory $output *> $log
        $code=$LASTEXITCODE
    } finally { $ErrorActionPreference=$savedPreference }
    if($expectFailure) {
        if($code -eq 0){throw 'Mismatched assembly bytes were accepted'}
        return
    }
    if($code -ne 0){Get-Content $log -Tail 8;throw ('Comparison failed: '+$name)}
    return Get-Content $output -Raw|ConvertFrom-Json
}
# These are deliberately synthetic report mutations; no assembly is patched.
$same=Compare-Receipt $ReportPath 'unchanged'
if(@($same.rows|Where-Object {$_.status -ne 'body_unchanged'}).Count -ne 0){throw 'Unchanged receipt produced a change'}
$receipt=Get-Content $ReportPath -Raw|ConvertFrom-Json
$removed=$receipt.patches[0]
$receipt.patches=@($receipt.patches|Where-Object {$_.patch -ne $removed.patch})
$receipt.classes--
$difference=Compare-Receipt (Write-Receipt $receipt 'removed-class') 'removed-class'
$deleted=@($difference.rows|Where-Object {$_.status -eq 'patch_removed'})
if($deleted.Count -ne $removed.targets.Count -or @($deleted|Where-Object {$_.patch -ne $removed.patch}).Count -ne 0){throw 'Whole patch removal was omitted or misidentified'}
$receipt=Get-Content $ReportPath -Raw|ConvertFrom-Json
$changed=$receipt.patches|Where-Object {$_.targets.Count -gt 1}|Select-Object -First 1
if($null -eq $changed){throw 'Fixture needs a multi-target class'}
$changed.targets=@($changed.targets|Select-Object -Skip 1)
$difference=Compare-Receipt (Write-Receipt $receipt 'removed-target') 'removed-target'
$deleted=@($difference.rows|Where-Object {$_.status -eq 'previous_target_missing'})
if($deleted.Count -ne 1 -or $deleted[0].patch -ne $changed.patch){throw 'Target removal was omitted or misidentified'}
$receipt=Get-Content $ReportPath -Raw|ConvertFrom-Json
$receipt.pluginSha256='0'*64
Compare-Receipt (Write-Receipt $receipt 'stale-bytes') 'stale-bytes' $true
Write-Host 'Hook comparison: unchanged, whole-class removal, target removal and assembly-byte mismatch controls passed.'
