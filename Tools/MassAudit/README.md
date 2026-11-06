# Read-only container mass audit

Run from the repository root:

```powershell
dotnet run --project Tools/MassAudit -c Release -- "save.zip" "obj\MASSFIX-COVERAGE"
```

Defaults to the `build/build.zip` save and `obj/MASSFIX-COVERAGE`. Uses installed
base definitions and the production `MassRepairRules` eligibility logic without
running Unity, applying loot, advancing simulation, or writing saves. The source
archive SHA-256 is verified before/after. Writes only the requested report folder.

The tool streams global CO records and ship layouts, accounts for saved stacks,
slots, lot exclusions and nested containers, then simulates native ancestor mass
deltas in deepest-first order. Source-condition values are used for cargo; only
eligible containers' tare is reconstructed. Duplicate/missing/invalid graphs and
explicit mass overrides are skipped. Definitions absent from the installed base
data, including overlays/mod-only definitions, are skipped rather than invented.

Coverage describes the entire saved world, including unloaded inventories, as
if normally instantiated. The live command only scans actual loaded world COs;
it additionally requires fully materialized (`Edit`/`Full`) ship inventories
and checks runtime freeze state and actual components. Therefore
save-wide coverage and predicted changes are not a promise that a single live
command touches all those objects. The audit does not identify the historical
cause of every discrepancy or measure live command performance.

`report.json` contains per-type coverage, skip reasons and predicted before/after
mass. Its README summarizes counts. Bodies and other ancestors can receive
native cargo deltas even though their own tare is never reconstructed. Direct
adjustment counts depend on traversal order; compare final mass instead.
