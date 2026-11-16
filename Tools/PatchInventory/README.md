# Static Harmony declaration inventory

The current 1.1.0.0 candidate has 297 decorated patch classes, including the
native parent-pivot placement repair, restored wheel/Shift-drag coexistence,
[shared SFF/PASS world stepping and end-turn drain](../../Orbital/PASS-TIME-SKIPS.md),
three classes covering five [administrative bounty-preservation boundaries](../../Navigation/BOUNTY-PRESERVATION.md),
and three [passive docking attachment](../../Navigation/DOCKED-ORBIT-LOCK.md) classes.
Historical counts below describe earlier source checkpoints. See the
[compatibility audit](../../Build/COMPATIBILITY-1.1.0.0.md) for actual binding,
native execution and live-acceptance distinctions.

Read the built plugin's metadata without loading it, invoking patch factories or
installing hooks. From the repository root:

```powershell
dotnet run --project Tools/PatchInventory -c Release -- <Ostrabus.dll> <BepInEx/core-directory>
```

For actual originals, first run the offline `Tests/PatchBindingCheck` harness
with `--report <report.json>`. This reports Harmony's installed original targets,
rather than treating generated replacement methods as originals. The inventory
tool can then compare two version-specific receipts or map a receipt to source,
feature ownership and candidate native suites:

```powershell
dotnet run --project Tools/PatchInventory -c Release -- compare-hooks <old-report.json> <new-report.json> <old-Managed> <new-Managed> <output.json>
dotnet run --project Tools/PatchInventory -c Release -- evidence <report.json> . <output.json>
```

The comparison fingerprints normalized direct target IL/contracts, including
branches, locals and exception regions. It resolves Ostrabus-owned targets from
each receipt's plugin path. Keep both actual version-specific DLLs available;
the old source tag alone is not a binary receipt. Direct equality does not prove
unchanged callers, data or transitive behavior.

Both receipts must still match their recorded game/plugin SHA-256 values.
Whole classes absent from the new receipt produce `patch_removed` rows; removed
targets within retained classes produce `previous_target_missing`. Removal is a
reported change, not automatic approval to retire a feature.

With a current full binding receipt and matching assemblies available, run the
synthetic comparison controls without executing game code:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Tools/PatchInventory/Test-HookComparison.ps1 -ReportPath <report.json> -ManagedDirectory <Managed-directory>
```

Controls cover unchanged input, whole-class removal, target removal and stale
assembly hashes. Old receipts without hashes need a fresh actual binding run;
the comparator does not stamp guessed historical hashes onto them.

Evidence mapping requires the matching portable PDB. It records source paths,
stable feature IDs and suites that link the source or name the type. Those
associations are navigation aids, not proof that every branch executes in a test.
The tool does not install runtime patches or change game files. Failures are
reported to stderr with a nonzero exit code.

The JSON lists HarmonyPatch-decorated types, target attribute arguments and
patch/factory method names. The count is **patch classes**, not installed hooks,
distinct patched native methods or recurring calls. TargetMethods factories can
expand to multiple targets. Prepare can disable classes. Diagnostic patches
installed manually at runtime are outside this inventory. Do not invoke patch
factories offline just to expand the count: some depend on initialized game data.

Output may be redirected to ignored `obj/patch-inventory.json`. The November 9
initial Debug DLL contained 165 decorated patch types. After the native-priority
return repair added one and target-observer consolidation removed one, the
inventory returned to 165; shared refill observers replace two regional
observers without increasing that count. The escape-setter repair adds one,
bringing the current inventory to 166. See the
[behavior audit](../../Notes/AI-BEHAVIOR-PATCH-AUDIT.md) for interpretation.
