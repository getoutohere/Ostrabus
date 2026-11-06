param([string[]]$SaveArchives = @())
$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
. (Join-Path $workspace 'Build\Find-Game.ps1')
$gameDirectory = Find-Ostranauts
Push-Location $workspace
try {
    dotnet build Tests/SaveJsonCompatibility/Host/MonoHost.csproj -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Mono host build failed' }
    dotnet build Tests/SaveJsonCompatibility/SaveJsonCompatibility.csproj -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw 'Compatibility harness build failed' }
    $hostDll = 'Tests/SaveJsonCompatibility/Host/bin/Release/net8.0/MonoHost.dll'
    $harnessDll = 'Tests/SaveJsonCompatibility/bin/Release/netstandard2.1/SaveJsonCompatibility.dll'
    dotnet $hostDll $harnessDll
    if ($LASTEXITCODE -ne 0) { throw 'Compatibility/integration checks failed' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($archive in $SaveArchives) {
        $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
        $fixture = Join-Path $workspace ('obj\SaveJsonCompatibility\' + $hash.Substring(0, 16))
        New-Item -ItemType Directory -Force $fixture | Out-Null
        $copy = Join-Path $fixture 'save.zip'
        Copy-Item -LiteralPath $archive -Destination $copy -Force
        $inputDirectory = Join-Path $fixture 'input'
        New-Item -ItemType Directory -Force $inputDirectory | Out-Null
        $zip = [IO.Compression.ZipFile]::OpenRead($copy)
        try {
            foreach ($entry in $zip.Entries) {
                if (-not $entry.FullName.EndsWith('.json', [StringComparison]::OrdinalIgnoreCase)) { continue }
                $target = [IO.Path]::GetFullPath((Join-Path $inputDirectory $entry.FullName))
                if (-not $target.StartsWith($inputDirectory + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Archive entry outside fixture directory' }
                New-Item -ItemType Directory -Force ([IO.Path]::GetDirectoryName($target)) | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
            }
        } finally { $zip.Dispose() }
        $report = Join-Path $fixture 'comparison.json'
        dotnet $hostDll $harnessDll validate $inputDirectory $report
        if ($LASTEXITCODE -ne 0) { throw "Save compatibility failed; see $report" }
        [pscustomobject]@{ ArchiveSha256 = $hash; GameAssemblySha256 = (Get-FileHash (Join-Path $gameDirectory 'Ostranauts_Data\Managed\Assembly-CSharp.dll')).Hash } | ConvertTo-Json | Set-Content (Join-Path $fixture 'input-hashes.json')
        Write-Output "Validated: $report"
    }
} finally { Pop-Location }
