$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$deployScript = Join-Path $workspace 'Deploy-Mod.ps1'
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($deployScript, [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
$definition = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Disable-LegacyPlugin' }, $true)
if ($null -eq $definition) { throw 'Deployment migration function missing.' }
# Load only the production helper, never the elevated deployment entry point.
. ([scriptblock]::Create($definition.Extent.Text))
$testRoot = Join-Path $workspace ('obj\DeployRenameCheck-' + [Guid]::NewGuid().ToString('N'))
$plugins = Join-Path $testRoot 'plugins'
$records = [Collections.Generic.List[object]]::new()
foreach ($relative in @('TemplateExample', 'Ostrabus', '')) {
    $folder = if ($relative) { Join-Path $plugins $relative } else { $plugins }
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    foreach ($name in @('TemplateExample.dll', 'TemplateExample.pdb')) { [IO.File]::WriteAllText((Join-Path $folder $name), 'legacy bytes') }
}
$current = Join-Path $plugins 'Ostrabus\Ostrabus.dll'
$unrelated = Join-Path $plugins 'Unrelated.dll'
[IO.File]::WriteAllText($current, 'new plugin')
[IO.File]::WriteAllText($unrelated, 'another mod')
Disable-LegacyPlugin -PluginsDirectory $plugins -DisabledFiles $records
if ($records.Count -ne 6) { throw 'Not all known predecessor paths were disabled.' }
foreach ($record in $records) {
    if (Test-Path -LiteralPath $record.Original) { throw 'Legacy assembly still active.' }
    if ($record.Backup.EndsWith('.dll') -or [IO.File]::ReadAllText($record.Backup) -ne 'legacy bytes') { throw 'Legacy backup not safe/recoverable.' }
}
Disable-LegacyPlugin -PluginsDirectory $plugins -DisabledFiles $records
if ($records.Count -ne 6) { throw 'Repeated deployment changed existing backups.' }
if ([IO.File]::ReadAllText($current) -ne 'new plugin' -or [IO.File]::ReadAllText($unrelated) -ne 'another mod') { throw 'Migration touched another assembly.' }
foreach ($record in $records) { Move-Item -LiteralPath $record.Backup -Destination $record.Original }
foreach ($record in $records) {
    if ([IO.File]::ReadAllText($record.Original) -ne 'legacy bytes') { throw 'Rollback failed.' }
}
Write-Output 'Deployment rename checks passed: predecessor isolation, reversible backups, idempotence, unrelated plugins and rollback.'
