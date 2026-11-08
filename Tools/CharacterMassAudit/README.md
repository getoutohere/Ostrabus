# Read-only character mass audit

```powershell
dotnet run --project Tools/CharacterMassAudit -c Release -- "save.zip" "obj/CharacterMassAudit" "character-id;another-id" "obj/character-graph.json"
```

The optional character list selects records for the terminal summary. The last
argument optionally exports their detached inventory graphs for repair testing. The
report covers saved humans with auditable physical layouts. No save, live game
or installed data is modified; SHA-256 is verified before and after. Reports
belong under ignored `obj/` because they contain private game state.

The tool reads installed crew, limb, overlay, trait and loot definitions. It
streams both global and per-ship CO arrays, including crew layouts, resolves
native `DEFAULT` records through base/overlay definitions, and counts stack
members once while excluding lot and duplicate layout aliases.

The initial comparison is core + current trait modifier + current immediate
owned-root masses. Those roots already include their nested contents. A second,
provisional figure rebuilds known anatomical components bottom-up; other cargo
masses remain as saved. Neither figure is an instruction to repair a character
while ignoring corrupt children. Unknown defaults, graphs or overrides are
skipped rather than guessed.

Native baseline tests are in `Tests/DollyMassCheck/CharacterBaselineDiagnosis.cs`.
To test the production repair against an exported graph without changing the save:

```powershell
dotnet build Tests/DollyMassCheck -c Release
dotnet run --project Tests/SaveJsonCompatibility/Host -c Release --no-build -- Tests/DollyMassCheck/bin/Release/netstandard2.1/DollyMassCheck.dll --graph "obj/character-graph.json"
```

The fixture reconstructs scalar conditions and ownership, then runs the actual
repair policy/planner and native mass bookkeeping. It checks a second run makes
no corrections and verifies the detached input hash remains unchanged.
See [findings and limits](../../MiscBugFixes/CHARACTER-MASS-RECONSTRUCTION.md).
