[CmdletBinding()]
param([string]$GameDirectory = $env:OSTRANAUTS_DIR)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Build\Find-Game.ps1')
$deploySource = Join-Path $PSScriptRoot 'bin\Debug\netstandard2.1'
if (-not (Test-Path -LiteralPath (Join-Path $deploySource 'Ostrabus.dll'))) { $deploySource = Join-Path $PSScriptRoot 'Plugin' }
$deployFiles = @('Ostrabus.dll', 'Ostrabus.pdb')
$disabledLegacy = [Collections.Generic.List[object]]::new()
$newDllCopied = $false
$deploymentComplete = $false

function Disable-LegacyPlugin {
    param([Parameter(Mandatory = $true)][string]$PluginsDirectory,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][Collections.Generic.List[object]]$DisabledFiles)
    # Only known predecessor filenames. Keep reversible backups and leave every
    # other plugin/asset alone; BepInEx discovers files ending in .dll.
    $pluginsRoot = [IO.Path]::GetFullPath($PluginsDirectory).TrimEnd('\')
    foreach ($relative in @('TemplateExample', 'Ostrabus', '')) {
        $folder = if ($relative) { Join-Path $pluginsRoot $relative } else { $pluginsRoot }
        foreach ($name in @('TemplateExample.dll', 'TemplateExample.pdb')) {
            $legacyPath = [IO.Path]::GetFullPath((Join-Path $folder $name))
            if (-not $legacyPath.StartsWith($pluginsRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Legacy path outside plugins directory: $legacyPath" }
            if (-not (Test-Path -LiteralPath $legacyPath -PathType Leaf)) { continue }
            $backupPath = $legacyPath + '.disabled-' + [Guid]::NewGuid().ToString('N')
            Move-Item -LiteralPath $legacyPath -Destination $backupPath
            $DisabledFiles.Add([pscustomobject]@{ Original = $legacyPath; Backup = $backupPath })
            Write-Host "Disabled predecessor $legacyPath (backup retained)."
        }
    }
}

try {
    $gamePath = Find-Ostranauts -GameDirectory $GameDirectory
    $deployDestination = Join-Path $gamePath 'BepInEx\plugins\Ostrabus'
    foreach ($deployFile in $deployFiles) {
        $deployPath = Join-Path $deploySource $deployFile
        if (-not (Test-Path -LiteralPath $deployPath -PathType Leaf)) {
            throw "Build output missing: $deployPath`nRebuild the project before deploying."
        }
    }

    $deployIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $deployPrincipal = New-Object Security.Principal.WindowsPrincipal($deployIdentity)
    if (-not $deployPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        $deployArguments = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -GameDirectory "{1}"' -f $PSCommandPath, $gamePath
        $deployProcess = Start-Process -FilePath "$PSHOME\powershell.exe" -ArgumentList $deployArguments -Verb RunAs -Wait -PassThru
        exit $deployProcess.ExitCode
    }

    if (Get-Process -Name 'Ostranauts' -ErrorAction SilentlyContinue) {
        throw 'Close Ostranauts before deploying so it can load the new library on startup.'
    }

    New-Item -ItemType Directory -Path $deployDestination -Force | Out-Null
    foreach ($deployFile in $deployFiles) {
        $deployFrom = Join-Path $deploySource $deployFile
        $deployTo = Join-Path $deployDestination $deployFile
        Copy-Item -LiteralPath $deployFrom -Destination $deployTo -Force
        if ($deployFile -eq 'Ostrabus.dll') { $newDllCopied = $true }
        if ((Get-FileHash -LiteralPath $deployFrom).Hash -ne (Get-FileHash -LiteralPath $deployTo).Hash) {
            throw "Copy verification failed: $deployFile"
        }
        Write-Host "Deployed $deployFile"
    }
    & (Join-Path $PSScriptRoot 'PdaSettings\Install-Assets.ps1') -AssetSource (Join-Path $deploySource 'PdaSettings\Assets') -GameDirectory $gamePath
    Disable-LegacyPlugin -PluginsDirectory (Split-Path -Parent $deployDestination) -DisabledFiles $disabledLegacy
    $deploymentComplete = $true
    Write-Host "`nInstalled to $deployDestination" -ForegroundColor Green
    Write-Host 'Start Ostranauts to load the new build.'
} catch {
    if ($newDllCopied -and -not $deploymentComplete) {
        # A failed rename/deploy must not leave both plugin assemblies active.
        $failedDll = Join-Path $deployDestination 'Ostrabus.dll'
        if (Test-Path -LiteralPath $failedDll -PathType Leaf) {
            Move-Item -LiteralPath $failedDll -Destination ($failedDll + '.failed-' + [Guid]::NewGuid().ToString('N'))
        }
        foreach ($legacy in $disabledLegacy) {
            if (Test-Path -LiteralPath $legacy.Backup -PathType Leaf) { Move-Item -LiteralPath $legacy.Backup -Destination $legacy.Original }
        }
    }
    Write-Host "`nDeployment failed: $($_.Exception.Message)" -ForegroundColor Red
    Read-Host 'Press Enter to close'
    exit 1
}

Read-Host 'Press Enter to close'
