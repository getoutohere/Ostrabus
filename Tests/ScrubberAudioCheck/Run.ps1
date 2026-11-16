[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Push-Location $repo
try {
    & dotnet build Tests/ScrubberAudioCheck -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Scrubber audio harness build failed.' }
    & dotnet build Tests/SaveJsonCompatibility/Host -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Native Mono host build failed.' }
    $previousPreference=$ErrorActionPreference
    try {
        $ErrorActionPreference='Continue'
        & dotnet Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll Tests/ScrubberAudioCheck/bin/Release/netstandard2.1/ScrubberAudioCheck.dll
        $nativeResult=$LASTEXITCODE
    } finally { $ErrorActionPreference=$previousPreference }
    if ($nativeResult -ne 0) { throw 'Native scrubber audio checks failed.' }

    # Disposable workspace fixture only; never target the real installation.
    $fixture=Join-Path $repo ('obj\ScrubberAudioCheck\install-'+[Guid]::NewGuid().ToString('N'))
    $game=Join-Path $fixture 'Game'
    $modRoot=Join-Path $game 'Ostranauts_Data\Mods'
    New-Item -ItemType Directory -Path $modRoot -Force | Out-Null
    $orderPath=Join-Path $modRoot 'loading_order.json'
    '[{"strName":"Mod Loading Order","aLoadOrder":["core","Unrelated|disabled"],"aIgnorePatterns":[]}]' | Set-Content -LiteralPath $orderPath
    $arguments=@{ModName='OstrabusScrubberAudio';AssetSource=(Join-Path $repo 'MiscBugFixes\ScrubberAudio\Assets');GameDirectory=$game;SettingsPath=(Join-Path $fixture 'no-settings.json')}
    $installer=Join-Path $repo 'Build\Install-NativeMod.ps1'
    & $installer @arguments
    & $installer @arguments
    $entries=@((Get-Content -LiteralPath $orderPath -Raw | ConvertFrom-Json)[0].aLoadOrder)
    if (@($entries | Where-Object {$_ -eq 'OstrabusScrubberAudio'}).Count -ne 1 -or $entries -notcontains 'Unrelated|disabled') { throw 'Installation duplicated entries or changed an unrelated mod.' }
    $source=Join-Path $arguments.AssetSource 'data\gasrespires\scrubber_audio.json'
    $installed=Join-Path $modRoot 'OstrabusScrubberAudio\data\gasrespires\scrubber_audio.json'
    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $installed).Hash) { throw 'Installed asset hash mismatch.' }
    & $installer @arguments -Disabled
    $entries=@((Get-Content -LiteralPath $orderPath -Raw | ConvertFrom-Json)[0].aLoadOrder)
    if ($entries -notcontains 'OstrabusScrubberAudio|disabled') { throw 'Omission did not disable the native mod.' }
    & $installer @arguments
    $entries=@((Get-Content -LiteralPath $orderPath -Raw | ConvertFrom-Json)[0].aLoadOrder)
    if ($entries -notcontains 'OstrabusScrubberAudio|disabled') { throw 'Ordinary full deployment failed to preserve user-disabled state.' }
    & $installer @arguments -Enable
    $entries=@((Get-Content -LiteralPath $orderPath -Raw | ConvertFrom-Json)[0].aLoadOrder)
    if ($entries -notcontains 'OstrabusScrubberAudio' -or $entries -notcontains 'Unrelated|disabled') { throw 'Custom selection did not re-enable only the selected native mod.' }
    Write-Host 'PASS: native asset installation, repeat installation, hashes, omission and re-enabling controls.'
} finally { Pop-Location }
